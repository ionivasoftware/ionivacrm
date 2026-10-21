using IonCrm.API.Common;
using IonCrm.Application.Features.Leads;
using IonCrm.Application.Features.Outreach;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Global (non-TR) lead-agent ingest — same static key as the TR agent (<c>X-Agent-Key</c> = LeadAgent:ApiKey).
/// Candidates land in <c>GlobalLeads</c>, never in Customers; dedupe runs here against every earlier global
/// lead of the same country (any status). Runs are recorded in LeadAgentRuns with Cities = "GB:Region, Region".
/// </summary>
[Route("api/v1/lead-agent/global")]
[AllowAnonymous]
public sealed class GlobalLeadAgentController : ApiControllerBase
{
    private readonly IGlobalLeadRepository _repo;
    private readonly ILeadCandidateRepository _runs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GlobalLeadAgentController> _logger;

    public GlobalLeadAgentController(IGlobalLeadRepository repo, ILeadCandidateRepository runs, IConfiguration configuration, ILogger<GlobalLeadAgentController> logger)
    {
        _repo = repo; _runs = runs; _configuration = configuration; _logger = logger;
    }

    /// <summary>GET /api/v1/lead-agent/global/next-regions?country=GB&amp;count=2 — least-recently scanned regions (90-day rotation).</summary>
    [HttpGet("next-regions")]
    public async Task<IActionResult> NextRegions([FromQuery] string country = "GB", [FromQuery] int count = 2, CancellationToken cancellationToken = default)
    {
        if (LeadAgentAuth.Check(Request, _configuration) is { } u) return u;
        var c = GlobalLeadNormalizer.NormalizeCountry(country);
        var recent = await _repo.RecentlyScannedRegionsAsync(c, DateTime.UtcNow.AddDays(-90), cancellationToken);
        return OkResponse(new { country = c, regions = GlobalLeadRegions.Next(c, Math.Clamp(count, 1, 10), recent) });
    }

    /// <summary>POST /api/v1/lead-agent/global/runs/start — {runId, country, regions[]}.</summary>
    [HttpPost("runs/start")]
    public async Task<IActionResult> StartRun([FromBody] StartGlobalRunBody body, CancellationToken cancellationToken = default)
    {
        if (LeadAgentAuth.Check(Request, _configuration) is { } u) return u;
        if (string.IsNullOrWhiteSpace(body.RunId) || body.RunId.Length > 60) return BadRequest(ApiResponse<object>.Fail("runId gerekli (≤60).", 400));
        var c = GlobalLeadNormalizer.NormalizeCountry(body.Country);
        var run = await _runs.GetRunAsync(body.RunId, cancellationToken);
        if (run is null)
        {
            run = new LeadAgentRun { RunId = body.RunId.Trim(), StartedAt = DateTime.UtcNow, Cities = $"{c}:" + string.Join(", ", (body.Regions ?? new()).Select(r => r.Trim()).Where(r => r.Length > 0)) };
            await _runs.AddRunAsync(run, cancellationToken);
        }
        return OkResponse(new { run.RunId, run.StartedAt, run.Cities });
    }

    /// <summary>POST /api/v1/lead-agent/global/candidates — {runId, country, region, items[]} → per-item created | duplicate | exists | invalid.</summary>
    [HttpPost("candidates")]
    public async Task<IActionResult> Submit([FromBody] SubmitGlobalLeadsBody body, CancellationToken cancellationToken = default)
    {
        if (LeadAgentAuth.Check(Request, _configuration) is { } u) return u;
        var items = body.Items ?? new();
        if (items.Count == 0) return BadRequest(ApiResponse<object>.Fail("items boş.", 400));
        if (items.Count > 200) return BadRequest(ApiResponse<object>.Fail("Tek istekte en fazla 200 aday.", 400));
        var country = GlobalLeadNormalizer.NormalizeCountry(body.Country);

        var known = (await _repo.KnownAsync(cancellationToken)).Select(k => new KnownCompany(k.Id, k.Name, k.NormalizedName, k.NormalizedPhone, k.Domain, null, k.City)).ToList();
        var run = string.IsNullOrWhiteSpace(body.RunId) ? null : await _runs.GetRunAsync(body.RunId, cancellationToken);
        var results = new List<LeadSubmitResult>();
        int created = 0, duplicates = 0, existing = 0, invalid = 0;

        foreach (var item in items)
        {
            var name = item.CompanyName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 300) { results.Add(new(item.CompanyName ?? "", "invalid", null, "Firma adı yok")); invalid++; continue; }
            var normName = GlobalLeadNormalizer.NormalizeName(name);
            var normPhone = GlobalLeadNormalizer.NormalizePhone(item.Phone, country);
            var domain = LeadNormalizer.DomainOf(item.Website) ?? LeadNormalizer.DomainOf(item.Email);
            var email = GlobalLeadNormalizer.IsUsableEmail(item.Email) ? item.Email!.Trim() : null;
            if (normName.Length < 2) { results.Add(new(name, "invalid", null, "İsim normalize edilemedi")); invalid++; continue; }
            if (email is null && domain is null) { results.Add(new(name, "invalid", null, "E-posta ya da web sitesi gerekli (outreach e-posta ile)")); invalid++; continue; }

            var fingerprint = GlobalLeadNormalizer.Fingerprint(country, normName, normPhone, domain);
            if (await _repo.FindByFingerprintAsync(fingerprint, cancellationToken) is { } same)
            { results.Add(new(name, "exists", same.Id, $"Daha önce önerildi ({same.Status})")); existing++; continue; }

            var verdict = LeadDedupe.Evaluate(normName, normPhone, domain, null, item.City, known);
            if (verdict.IsDuplicate) { results.Add(new(name, "exists", verdict.MatchId, verdict.Notes)); existing++; continue; }

            var lead = new GlobalLead
            {
                Country = country, Region = Trunc(item.Region ?? body.Region, 120), CompanyName = name, NormalizedName = Trunc(normName, 300)!,
                ContactName = Trunc(item.ContactName, 200), Email = email, Domain = domain, Phone = Trunc(item.Phone, 60), NormalizedPhone = normPhone.Length == 0 ? null : normPhone,
                Website = Trunc(item.Website, 500), Address = Trunc(item.Address, 600), City = Trunc(item.City, 120),
                Source = Trunc(string.IsNullOrWhiteSpace(item.Source) ? "web" : item.Source, 120)!, SourceUrl = Trunc(item.SourceUrl, 1000),
                Snippet = item.Snippet is { Length: > 2000 } sn ? sn[..2000] : item.Snippet, Confidence = Math.Clamp(item.Confidence ?? 50, 0, 100),
                Fingerprint = fingerprint, AgentRunId = Trunc(body.RunId, 60), Status = GlobalLeadStatus.Pending,
                PossibleMatchLeadId = verdict.PossibleMatchId, MatchNotes = string.IsNullOrEmpty(verdict.Notes) ? null : verdict.Notes,
                UnsubscribeToken = OutreachSequence.NewUnsubscribeToken(),
            };
            await _repo.AddAsync(lead, cancellationToken);
            known.Add(new KnownCompany(lead.Id, name, normName, lead.NormalizedPhone, domain, null, lead.City));
            created++; results.Add(new(name, "created", lead.Id, verdict.Notes.Length == 0 ? null : verdict.Notes));
        }

        if (run is not null)
        {
            run.Found += items.Count; run.Created += created; run.Duplicates += duplicates; run.Existing += existing; run.Invalid += invalid;
            await _runs.UpdateRunAsync(run, cancellationToken);
        }
        _logger.LogInformation("GlobalLeadAgent: {Run} {Country}/{Region} — {Found} aday: {Created} yeni, {Exist} önceden, {Invalid} geçersiz", body.RunId, country, body.Region, items.Count, created, existing, invalid);
        return OkResponse(new { created, duplicates, existing, invalid, results });
    }

    /// <summary>POST /api/v1/lead-agent/global/runs/{runId}/finish — {notes?}.</summary>
    [HttpPost("runs/{runId}/finish")]
    public async Task<IActionResult> FinishRun(string runId, [FromBody] FinishLeadRunBody? body, CancellationToken cancellationToken = default)
    {
        if (LeadAgentAuth.Check(Request, _configuration) is { } u) return u;
        var run = await _runs.GetRunAsync(runId, cancellationToken);
        if (run is null) return NotFound(ApiResponse<object>.Fail("Koşu bulunamadı.", 404));
        run.FinishedAt = DateTime.UtcNow; run.Notes = body?.Notes is { Length: > 2000 } n ? n[..2000] : body?.Notes;
        await _runs.UpdateRunAsync(run, cancellationToken);
        return OkResponse(new { run.RunId, run.Found, run.Created, run.Existing, run.Invalid, run.FinishedAt });
    }

    private static string? Trunc(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());
}

public record GlobalLeadItem(
    string? CompanyName, string? ContactName, string? Phone, string? Email, string? Website, string? Address, string? City, string? Region,
    string? Source, string? SourceUrl, string? Snippet, int? Confidence);

public record SubmitGlobalLeadsBody(string? RunId, string? Country, string? Region, List<GlobalLeadItem>? Items);

public record StartGlobalRunBody(string RunId, string? Country, List<string>? Regions);
