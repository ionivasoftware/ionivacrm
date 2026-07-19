using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// SuperAdmin-only proxy over the external error-triage queues (RezervAl + Liftdesk/EMS).
///
/// GET   /api/v1/error-triage                       — merged card list from all configured sources
/// PATCH /api/v1/error-triage/{source}/{id}/status  — approve / reject a card on its source system
///
/// Credentials never reach the browser: RezervAl auth is an apiKey→JWT exchange inside
/// <see cref="ISaasBClient"/>; Liftdesk auth is a static M2M Bearer key inside <see cref="ILiftdeskClient"/>.
/// Cards are normalised into <see cref="ErrorTriageCardDto"/> with a <c>source</c> discriminator so the
/// UI renders both queues identically (badge per source).
/// </summary>
[Route("api/v1/error-triage")]
[Authorize(Policy = "SuperAdmin")]
public sealed class ErrorTriageController : ApiControllerBase
{
    private readonly ISaasBClient _saasBClient;
    private readonly ILiftdeskClient _liftdeskClient;
    private readonly IConfiguration _configuration;
    private readonly IProjectRepository _projectRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ErrorTriageController> _logger;

    /// <summary>Initialises a new instance of <see cref="ErrorTriageController"/>.</summary>
    public ErrorTriageController(
        ISaasBClient saasBClient,
        ILiftdeskClient liftdeskClient,
        IConfiguration configuration,
        IProjectRepository projectRepository,
        ICurrentUserService currentUser,
        ILogger<ErrorTriageController> logger)
    {
        _saasBClient       = saasBClient;
        _liftdeskClient    = liftdeskClient;
        _configuration     = configuration;
        _projectRepository = projectRepository;
        _currentUser       = currentUser;
        _logger            = logger;
    }

    /// <summary>
    /// Resolves the RezervAl CRM API key (exchanged for a JWT by the client).
    /// Prefers the global <c>SaasB:ApiKey</c> config; falls back to the first project that has a
    /// <c>RezervAlApiKey</c> configured — the same credential the rest of the RezervAl integration uses.
    /// </summary>
    private async Task<string?> ResolveRezervalApiKeyAsync(CancellationToken ct)
    {
        var configured = _configuration["SaasB:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var projects = await _projectRepository.GetAllAsync(ct);
        return projects
            .Select(p => p.RezervAlApiKey)
            .FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));
    }

    /// <summary>
    /// Maps a CRM tab status onto the EMS statuses it covers. EMS has a richer state machine
    /// (Fixing between Approved and Fixed, Failed as a fix-agent dead end); the UI keeps the four
    /// RezervAl-style tabs and shows the precise EMS status as a badge on each card.
    /// </summary>
    private static string[] LiftdeskStatusesFor(string tab) => tab switch
    {
        "Approved" => new[] { "Approved", "Fixing" },
        "Fixed"    => new[] { "Fixed", "Failed" },
        _          => new[] { tab },
    };

    private static int SeverityRank(string? severity) => severity switch
    {
        "Critical" => 4,
        "High"     => 3,
        "Medium"   => 2,
        "Low"      => 1,
        _          => 0,
    };

    /// <summary>
    /// Lists error cards from every configured source, merged and sorted by severity, recurrence
    /// and recency. A source that fails or lacks credentials degrades to a warning in
    /// <c>message</c> instead of failing the whole request.
    /// GET /api/v1/error-triage?status=Triaged&amp;page=1&amp;pageSize=50
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ErrorTriageCardDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCards(
        [FromQuery] string status = "Triaged",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page     = page     < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;
        status   = string.IsNullOrWhiteSpace(status) ? "Triaged" : status.Trim();

        var rezervalKey = await ResolveRezervalApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rezervalKey) && !_liftdeskClient.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail(
                "Hiçbir hata-triage kaynağı yapılandırılmamış (RezervAl / Liftdesk API anahtarı eksik).", 400));

        var rezervalTask = FetchRezervalAsync(rezervalKey, status, page, pageSize, cancellationToken);
        var liftdeskTask = FetchLiftdeskAsync(status, page, pageSize, cancellationToken);
        await Task.WhenAll(rezervalTask, liftdeskTask);

        var (rezervalCards, rezervalWarning) = rezervalTask.Result;
        var (liftdeskCards, liftdeskWarning) = liftdeskTask.Result;

        // DistinctBy guards against a card being returned by two Liftdesk status fetches when the
        // fix agent transitions it (Approved→Fixing) between the sequential calls.
        var cards = rezervalCards.Concat(liftdeskCards)
            .DistinctBy(c => (c.Source, c.Id))
            .OrderByDescending(c => SeverityRank(c.Severity))
            .ThenByDescending(c => c.OccurrenceCount)
            .ThenByDescending(c => c.CreatedOn ?? DateTime.MinValue)
            .ToList();

        var warnings = new[] { rezervalWarning, liftdeskWarning }
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        return OkResponse(cards, warnings.Count > 0 ? string.Join(" · ", warnings) : null);
    }

    private async Task<(List<ErrorTriageCardDto> Cards, string? Warning)> FetchRezervalAsync(
        string? apiKey, string status, int page, int pageSize, CancellationToken ct)
    {
        // No key → the source simply isn't wired up; skip silently like Liftdesk does.
        // (GetCards already 400s when NO source is configured at all.)
        if (string.IsNullOrWhiteSpace(apiKey))
            return (new List<ErrorTriageCardDto>(), null);

        try
        {
            var result = await _saasBClient.GetErrorTriageAsync(status, page, pageSize, apiKey, ct);
            if (!result.IsSuccess)
            {
                var msg = result.ErrorResponse?.Message ?? result.Message ?? "hata kartları alınamadı.";
                return (new List<ErrorTriageCardDto>(), $"Rezerval: {msg}");
            }

            var cards = (result.Data ?? new List<RezervalErrorTriageCard>()).Select(ToDto).ToList();
            return (cards, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RezervAl error-triage listesi alınamadı.");
            return (new List<ErrorTriageCardDto>(), $"Rezerval bağlantı hatası: {ex.Message}");
        }
    }

    private async Task<(List<ErrorTriageCardDto> Cards, string? Warning)> FetchLiftdeskAsync(
        string status, int page, int pageSize, CancellationToken ct)
    {
        // No key → the source simply isn't wired up yet; don't warn on every poll.
        if (!_liftdeskClient.IsConfigured)
            return (new List<ErrorTriageCardDto>(), null);

        var cards = new List<ErrorTriageCardDto>();
        string? warning = null;

        // Per-status try/catch: a transport failure on the second fetch (timeout, broken circuit)
        // must not discard the cards the first fetch already returned.
        foreach (var emsStatus in LiftdeskStatusesFor(status))
        {
            try
            {
                var envelope = await _liftdeskClient.GetErrorAnalysesAsync(emsStatus, page, pageSize, ct);
                if (!envelope.Success)
                {
                    warning = $"Liftdesk: {envelope.Message ?? "hata kartları alınamadı."}";
                    continue;
                }

                cards.AddRange((envelope.Data?.Items ?? new List<LiftdeskErrorAnalysis>()).Select(ToDto));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Liftdesk error-triage listesi alınamadı (status={Status}).", emsStatus);
                warning = $"Liftdesk bağlantı hatası: {ex.Message}";
            }
        }

        return (cards, warning);
    }

    /// <summary>
    /// Approves or rejects a triaged card on its source system.
    /// PATCH /api/v1/error-triage/{source}/{id}/status
    /// Body: { "status": "Approved" | "Rejected", "rejectReason": "..." (Liftdesk reject) }
    /// <c>approvedBy</c> is derived from the authenticated SuperAdmin (not client-supplied) to prevent spoofing.
    /// </summary>
    [HttpPatch("{source}/{id}/status")]
    [ProducesResponseType(typeof(ApiResponse<ErrorTriageCardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateStatus(
        string source,
        string id,
        [FromBody] UpdateErrorTriageStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        var status = body?.Status?.Trim();
        if (status is not ("Approved" or "Rejected"))
            return BadRequest(ApiResponse<object>.Fail("Geçersiz durum. 'Approved' veya 'Rejected' olmalı.", 400));

        // Identify the approver from the JWT, falling back to a generic label.
        var approvedBy = !string.IsNullOrWhiteSpace(_currentUser.Email) ? _currentUser.Email : "crm";
        var successMsg = status == "Approved" ? "Kart onaylandı." : "Kart reddedildi.";

        if (string.Equals(source, "Rezerval", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(id, out var triageId))
                return BadRequest(ApiResponse<object>.Fail("Geçersiz Rezerval kart kimliği.", 400));

            var apiKey = await ResolveRezervalApiKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(apiKey))
                return BadRequest(ApiResponse<object>.Fail("RezervAl API anahtarı yapılandırılmamış.", 400));

            try
            {
                var result = await _saasBClient.UpdateErrorTriageStatusAsync(
                    triageId, status, approvedBy, apiKey, cancellationToken);

                if (!result.IsSuccess || result.Data is null)
                {
                    var msg = result.ErrorResponse?.Message ?? result.Message ?? "İşlem RezervAl tarafından reddedildi.";
                    return BadRequest(ApiResponse<object>.Fail(msg, 400));
                }

                return OkResponse(ToDto(result.Data), successMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RezervAl error-triage {TriageId} güncellenemedi.", triageId);
                return BadRequest(ApiResponse<object>.Fail($"RezervAl bağlantı hatası: {ex.Message}", 400));
            }
        }

        if (string.Equals(source, "Liftdesk", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(id, out var analysisId))
                return BadRequest(ApiResponse<object>.Fail("Geçersiz Liftdesk kart kimliği.", 400));

            if (!_liftdeskClient.IsConfigured)
                return BadRequest(ApiResponse<object>.Fail("Liftdesk API anahtarı yapılandırılmamış.", 400));

            // EMS requires a reject reason; default it so a quick "Reddet" without typing still works.
            var rejectReason = status == "Rejected"
                ? (string.IsNullOrWhiteSpace(body!.RejectReason) ? "CRM üzerinden reddedildi." : body.RejectReason.Trim())
                : null;

            try
            {
                var result = await _liftdeskClient.UpdateErrorAnalysisStatusAsync(
                    analysisId, status, approvedBy, rejectReason, cancellationToken);

                if (!result.Success || result.Data is null)
                    return BadRequest(ApiResponse<object>.Fail(
                        result.Message ?? "İşlem Liftdesk tarafından reddedildi.", 400));

                return OkResponse(ToDto(result.Data), successMsg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Liftdesk error-analysis {Id} güncellenemedi.", analysisId);
                return BadRequest(ApiResponse<object>.Fail($"Liftdesk bağlantı hatası: {ex.Message}", 400));
            }
        }

        return BadRequest(ApiResponse<object>.Fail("Geçersiz kaynak. 'Rezerval' veya 'Liftdesk' olmalı.", 400));
    }

    // ── Mapping ─────────────────────────────────────────────────────────────

    private static ErrorTriageCardDto ToDto(RezervalErrorTriageCard c) => new(
        Id:              c.TriageId.ToString(),
        Source:          "Rezerval",
        Fingerprint:     c.Fingerprint,
        OccurrenceCount: c.OccurrenceCount,
        Status:          c.Status,
        Severity:        c.Severity,
        RootCause:       c.RootCause,
        SuggestedFix:    c.SuggestedFix,
        SourceFile:      c.SourceFile,
        Exception:       c.Exception,
        TypeName:        c.TypeName,
        Component:       null,
        CreatedOn:       c.CreatedOn,
        UpdatedOn:       c.UpdatedOn,
        ApprovedBy:      c.ApprovedBy,
        RejectReason:    null,
        FixPrUrl:        null,
        FailReason:      null);

    private static ErrorTriageCardDto ToDto(LiftdeskErrorAnalysis a) => new(
        Id:              a.Id.ToString(),
        Source:          "Liftdesk",
        Fingerprint:     a.ClientErrorId.ToString(),
        OccurrenceCount: Math.Max(a.OccurrenceCount, a.ClientError?.OccurrenceCount ?? 0),
        Status:          a.Status,
        Severity:        a.Severity,
        RootCause:       a.RootCause,
        SuggestedFix:    a.SuggestedFix,
        SourceFile:      a.SourceFile,
        Exception:       BuildExceptionText(a.ClientError),
        TypeName:        a.ClientError?.ErrorType,
        Component:       a.ClientError?.Source,
        CreatedOn:       a.CreatedAt,
        UpdatedOn:       a.FixedAt ?? a.ReviewedAt ?? a.ApprovedOn,
        ApprovedBy:      a.ApprovedBy,
        RejectReason:    a.RejectReason,
        FixPrUrl:        a.FixPrUrl,
        FailReason:      a.FailReason);

    /// <summary>Combines the short message and the stack details, avoiding duplication when the
    /// message is already the first line of the details.</summary>
    private static string? BuildExceptionText(LiftdeskClientError? e)
    {
        if (e is null) return null;
        if (string.IsNullOrWhiteSpace(e.Details)) return e.Message;
        if (string.IsNullOrWhiteSpace(e.Message) || e.Details.Contains(e.Message)) return e.Details;
        return $"{e.Message}\n{e.Details}";
    }
}

/// <summary>
/// Source-agnostic error card served to the CRM UI. <c>Id</c> is the source system's identifier
/// (RezervAl: numeric triageId; Liftdesk: guid) and is only meaningful together with <c>Source</c>.
/// </summary>
public record ErrorTriageCardDto(
    string Id,
    string Source,           // "Rezerval" | "Liftdesk"
    string? Fingerprint,
    int OccurrenceCount,
    string? Status,          // Triaged | Approved | Rejected | Fixed (+ Liftdesk: Fixing | Failed)
    string? Severity,
    string? RootCause,
    string? SuggestedFix,
    string? SourceFile,
    string? Exception,
    string? TypeName,
    string? Component,       // Liftdesk only: Backend | Frontend | CustomerPortal | ...
    DateTime? CreatedOn,
    DateTime? UpdatedOn,
    string? ApprovedBy,
    string? RejectReason,    // Liftdesk only
    string? FixPrUrl,        // Liftdesk only — PR link once the fix agent delivers
    string? FailReason);     // Liftdesk only — why the fix agent gave up

/// <summary>Request body for PATCH /api/v1/error-triage/{source}/{id}/status.</summary>
public record UpdateErrorTriageStatusRequest(string Status, string? RejectReason = null);
