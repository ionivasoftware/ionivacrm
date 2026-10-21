using IonCrm.API.Common;
using IonCrm.Application.Features.Leads;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Lead-discovery agent ingest (GitHub Actions job, tools/lead-agent). Auth: static key in
/// <c>X-Agent-Key</c> (or Bearer) compared with LeadAgent:ApiKey — no user session. The agent only
/// proposes; every candidate lands in the SuperAdmin queue and dedupe runs here on arrival against all
/// customers and all earlier candidates (rejected ones included), so the same firm is never proposed twice.
/// </summary>
[Route("api/v1/lead-agent")]
[AllowAnonymous]
public sealed class LeadAgentController : ApiControllerBase
{
    private readonly ILeadCandidateRepository _repo;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LeadAgentController> _logger;

    public LeadAgentController(ILeadCandidateRepository repo, IConfiguration configuration, ILogger<LeadAgentController> logger)
    {
        _repo = repo; _configuration = configuration; _logger = logger;
    }

    /// <summary>GET /api/v1/lead-agent/next-cities?count=2 — least-recently scanned provinces (60-day rotation).</summary>
    [HttpGet("next-cities")]
    public async Task<IActionResult> NextCities([FromQuery] int count = 2, CancellationToken cancellationToken = default)
    {
        if (Unauthorized_() is { } u) return u;
        var recent = await _repo.RecentlyScannedCitiesAsync(DateTime.UtcNow.AddDays(-60), cancellationToken);
        var cities = LeadAgentCities.Next(Math.Clamp(count, 1, 10), recent);
        if (cities.Count == 0) cities = LeadAgentCities.Next(Math.Clamp(count, 1, 10), new HashSet<string>()); // tam tur bitti → baştan
        return OkResponse(new { cities });
    }

    /// <summary>POST /api/v1/lead-agent/runs/start — {runId, cities[]}.</summary>
    [HttpPost("runs/start")]
    public async Task<IActionResult> StartRun([FromBody] StartLeadRunBody body, CancellationToken cancellationToken = default)
    {
        if (Unauthorized_() is { } u) return u;
        if (string.IsNullOrWhiteSpace(body.RunId) || body.RunId.Length > 60) return BadRequest(ApiResponse<object>.Fail("runId gerekli (≤60).", 400));
        var run = await _repo.GetRunAsync(body.RunId, cancellationToken);
        if (run is null)
        {
            run = new LeadAgentRun { RunId = body.RunId.Trim(), StartedAt = DateTime.UtcNow, Cities = string.Join(", ", (body.Cities ?? new()).Select(c => c.Trim()).Where(c => c.Length > 0)) };
            await _repo.AddRunAsync(run, cancellationToken);
        }
        return OkResponse(new { run.RunId, run.StartedAt, run.Cities });
    }

    /// <summary>POST /api/v1/lead-agent/candidates — {runId, city, items[]} → per-item created | duplicate | exists | invalid.</summary>
    [HttpPost("candidates")]
    public async Task<IActionResult> Submit([FromBody] SubmitLeadCandidatesBody body, CancellationToken cancellationToken = default)
    {
        if (Unauthorized_() is { } u) return u;
        var items = body.Items ?? new();
        if (items.Count == 0) return BadRequest(ApiResponse<object>.Fail("items boş.", 400));
        if (items.Count > 200) return BadRequest(ApiResponse<object>.Fail("Tek istekte en fazla 200 aday.", 400));

        var projectId = await _repo.ResolveDefaultProjectIdAsync(Guid.TryParse(_configuration["LeadAgent:ProjectId"], out var cfg) ? cfg : null, cancellationToken);
        if (projectId is null) return StatusCode(503, ApiResponse<object>.Fail("Hedef proje bulunamadı.", 503));

        // Known companies: every customer (all projects) + every earlier candidate (any status).
        var known = new List<KnownCompany>();
        foreach (var c in await _repo.KnownCustomersAsync(cancellationToken))
            known.Add(new KnownCompany(c.Id, c.Name, LeadNormalizer.NormalizeName(c.Name), LeadNormalizer.NormalizePhone(c.Phone), LeadNormalizer.DomainOf(c.Email), c.TaxNumber, CityOf(c.Address)));
        var knownCustomerIds = known.Select(k => k.Id).ToHashSet();
        foreach (var c in await _repo.KnownCandidatesAsync(cancellationToken))
            known.Add(new KnownCompany(c.Id, c.Name, c.NormalizedName, c.NormalizedPhone, c.Domain, c.TaxNumber, c.City));

        var run = string.IsNullOrWhiteSpace(body.RunId) ? null : await _repo.GetRunAsync(body.RunId, cancellationToken);
        var results = new List<LeadSubmitResult>();
        int created = 0, duplicates = 0, existing = 0, invalid = 0;

        foreach (var item in items)
        {
            var name = item.CompanyName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 300) { results.Add(new(item.CompanyName ?? "", "invalid", null, "Firma adı yok")); invalid++; continue; }
            var normName = LeadNormalizer.NormalizeName(name);
            var normPhone = LeadNormalizer.NormalizePhone(item.Phone);
            var domain = LeadNormalizer.DomainOf(item.Website) ?? LeadNormalizer.DomainOf(item.Email);
            if (normName.Length < 2) { results.Add(new(name, "invalid", null, "İsim normalize edilemedi")); invalid++; continue; }
            if (string.IsNullOrEmpty(normPhone) && domain is null) { results.Add(new(name, "invalid", null, "Telefon ya da web sitesi gerekli")); invalid++; continue; }

            var fingerprint = LeadNormalizer.Fingerprint(normName, normPhone, domain);
            if (await _repo.FindByFingerprintAsync(fingerprint, cancellationToken) is { } same)
            { results.Add(new(name, "exists", same.Id, $"Daha önce önerildi ({same.Status})")); existing++; continue; }

            var verdict = LeadDedupe.Evaluate(normName, normPhone, domain, item.TaxNumber, item.City, known);
            // Matched an earlier *candidate* (not a customer) → that's a re-proposal, not a customer duplicate.
            if (verdict.IsDuplicate && verdict.MatchId is { } mid && !knownCustomerIds.Contains(mid))
            { results.Add(new(name, "exists", mid, verdict.Notes)); existing++; continue; }

            var candidate = new LeadCandidate
            {
                ProjectId = projectId.Value, CompanyName = name, NormalizedName = normName, LegalTitle = Trunc(item.LegalTitle, 400),
                Phone = Trunc(item.Phone, 60), NormalizedPhone = normPhone.Length == 0 ? null : normPhone, Email = Trunc(item.Email, 200),
                Website = Trunc(item.Website, 500), Domain = domain, Address = Trunc(item.Address, 600), City = Trunc(item.City ?? body.City, 80),
                District = Trunc(item.District, 80), TaxNumber = Trunc(item.TaxNumber, 30), MersisNo = Trunc(item.MersisNo, 30),
                Source = Trunc(string.IsNullOrWhiteSpace(item.Source) ? "web" : item.Source, 120)!, SourceUrl = Trunc(item.SourceUrl, 1000),
                Snippet = item.Snippet is { Length: > 2000 } sn ? sn[..2000] : item.Snippet, Confidence = Math.Clamp(item.Confidence ?? 50, 0, 100),
                Fingerprint = fingerprint, AgentRunId = Trunc(body.RunId, 60), SearchCity = Trunc(body.City, 80),
                Status = verdict.IsDuplicate ? LeadCandidateStatus.Duplicate : LeadCandidateStatus.Pending,
                DuplicateOfCustomerId = verdict.IsDuplicate ? verdict.MatchId : null,
                PossibleMatchCustomerId = verdict.PossibleMatchId, MatchNotes = string.IsNullOrEmpty(verdict.Notes) ? null : verdict.Notes,
            };
            await _repo.AddAsync(candidate, cancellationToken);
            known.Add(new KnownCompany(candidate.Id, name, normName, candidate.NormalizedPhone, domain, candidate.TaxNumber, candidate.City));

            if (verdict.IsDuplicate) { duplicates++; results.Add(new(name, "duplicate", candidate.Id, verdict.Notes)); }
            else { created++; results.Add(new(name, "created", candidate.Id, verdict.Notes.Length == 0 ? null : verdict.Notes)); }
        }

        if (run is not null)
        {
            run.Found += items.Count; run.Created += created; run.Duplicates += duplicates; run.Existing += existing; run.Invalid += invalid;
            await _repo.UpdateRunAsync(run, cancellationToken);
        }
        _logger.LogInformation("LeadAgent: {Run} {City} — {Found} aday: {Created} yeni, {Dup} mükerrer, {Exist} önceden, {Invalid} geçersiz",
            body.RunId, body.City, items.Count, created, duplicates, existing, invalid);
        return OkResponse(new { created, duplicates, existing, invalid, results });
    }

    /// <summary>POST /api/v1/lead-agent/runs/{runId}/finish — {notes?}.</summary>
    [HttpPost("runs/{runId}/finish")]
    public async Task<IActionResult> FinishRun(string runId, [FromBody] FinishLeadRunBody? body, CancellationToken cancellationToken = default)
    {
        if (Unauthorized_() is { } u) return u;
        var run = await _repo.GetRunAsync(runId, cancellationToken);
        if (run is null) return NotFound(ApiResponse<object>.Fail("Koşu bulunamadı.", 404));
        run.FinishedAt = DateTime.UtcNow; run.Notes = body?.Notes is { Length: > 2000 } n ? n[..2000] : body?.Notes;
        await _repo.UpdateRunAsync(run, cancellationToken);
        return OkResponse(new { run.RunId, run.Found, run.Created, run.Duplicates, run.Existing, run.Invalid, run.FinishedAt });
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private IActionResult? Unauthorized_() => LeadAgentAuth.Check(Request, _configuration);

    private static string? Trunc(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    /// <summary>Best-effort province from a free-text address: the last token that is a known province.</summary>
    private static string? CityOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var folded = LeadNormalizer.Fold(address);
        foreach (var city in LeadAgentCities.All)
            if (folded.Contains(LeadNormalizer.Fold(city))) return city;
        return null;
    }
}

public record LeadCandidateItem(
    string? CompanyName, string? LegalTitle, string? Phone, string? Email, string? Website, string? Address, string? City, string? District,
    string? TaxNumber, string? MersisNo, string? Source, string? SourceUrl, string? Snippet, int? Confidence);

public record SubmitLeadCandidatesBody(string? RunId, string? City, List<LeadCandidateItem>? Items);

public record LeadSubmitResult(string CompanyName, string Status, Guid? CandidateId, string? Reason);

public record StartLeadRunBody(string RunId, List<string>? Cities);

public record FinishLeadRunBody(string? Notes);
