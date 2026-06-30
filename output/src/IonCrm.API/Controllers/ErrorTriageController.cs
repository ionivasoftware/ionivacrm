using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// SuperAdmin-only proxy to the RezervAl error-triage queue.
///
/// GET   /api/v1/error-triage              — list triaged error cards
/// PATCH /api/v1/error-triage/{id}/status  — approve / reject a card
///
/// The RezervAl CRM API key (and the short-lived JWT exchanged from it) stays server-side —
/// the browser only ever talks to this CRM API with its normal JWT. RezervAl auth is handled
/// inside <see cref="ISaasBClient"/> (POST /v1/Token/GetToken → Bearer token, cached).
/// </summary>
[Route("api/v1/error-triage")]
[Authorize(Policy = "SuperAdmin")]
public sealed class ErrorTriageController : ApiControllerBase
{
    private readonly ISaasBClient _saasBClient;
    private readonly IConfiguration _configuration;
    private readonly IProjectRepository _projectRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ErrorTriageController> _logger;

    /// <summary>Initialises a new instance of <see cref="ErrorTriageController"/>.</summary>
    public ErrorTriageController(
        ISaasBClient saasBClient,
        IConfiguration configuration,
        IProjectRepository projectRepository,
        ICurrentUserService currentUser,
        ILogger<ErrorTriageController> logger)
    {
        _saasBClient       = saasBClient;
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
    private async Task<string?> ResolveApiKeyAsync(CancellationToken ct)
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
    /// Lists triaged error cards from RezervAl.
    /// GET /api/v1/error-triage?status=Triaged&amp;page=1&amp;pageSize=50
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<RezervalErrorTriageCard>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCards(
        [FromQuery] string status = "Triaged",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest(ApiResponse<object>.Fail("RezervAl API anahtarı yapılandırılmamış.", 400));

        page     = page     < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        try
        {
            var result = await _saasBClient.GetErrorTriageAsync(status, page, pageSize, apiKey, cancellationToken);

            if (!result.IsSuccess)
            {
                var msg = result.ErrorResponse?.Message ?? result.Message ?? "RezervAl hata kartları alınamadı.";
                return BadRequest(ApiResponse<object>.Fail(msg, 400));
            }

            return OkResponse(result.Data ?? new List<RezervalErrorTriageCard>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RezervAl error-triage listesi alınamadı.");
            return BadRequest(ApiResponse<object>.Fail($"RezervAl bağlantı hatası: {ex.Message}", 400));
        }
    }

    /// <summary>
    /// Approves or rejects a triaged error card.
    /// PATCH /api/v1/error-triage/{triageId}/status  Body: { "status": "Approved" | "Rejected" }
    /// <c>approvedBy</c> is derived from the authenticated SuperAdmin (not client-supplied) to prevent spoofing.
    /// </summary>
    [HttpPatch("{triageId:int}/status")]
    [ProducesResponseType(typeof(ApiResponse<RezervalErrorTriageCard>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateStatus(
        int triageId,
        [FromBody] UpdateErrorTriageStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest(ApiResponse<object>.Fail("RezervAl API anahtarı yapılandırılmamış.", 400));

        var status = body?.Status?.Trim();
        if (status is not ("Approved" or "Rejected"))
            return BadRequest(ApiResponse<object>.Fail("Geçersiz durum. 'Approved' veya 'Rejected' olmalı.", 400));

        // Identify the approver from the JWT, falling back to email then a generic label.
        var approvedBy = !string.IsNullOrWhiteSpace(_currentUser.Email) ? _currentUser.Email : "crm";

        try
        {
            var result = await _saasBClient.UpdateErrorTriageStatusAsync(
                triageId, status, approvedBy, apiKey, cancellationToken);

            if (!result.IsSuccess || result.Data is null)
            {
                var msg = result.ErrorResponse?.Message ?? result.Message ?? "İşlem RezervAl tarafından reddedildi.";
                return BadRequest(ApiResponse<object>.Fail(msg, 400));
            }

            return OkResponse(result.Data, status == "Approved" ? "Kart onaylandı." : "Kart reddedildi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RezervAl error-triage {TriageId} güncellenemedi.", triageId);
            return BadRequest(ApiResponse<object>.Fail($"RezervAl bağlantı hatası: {ex.Message}", 400));
        }
    }
}

/// <summary>Request body for PATCH /api/v1/error-triage/{triageId}/status.</summary>
public record UpdateErrorTriageStatusRequest(string Status);
