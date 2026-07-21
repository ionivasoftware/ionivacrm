using IonCrm.API.Common;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// SuperAdmin-only proxy to the Liftdesk (EMS) pricing management API. Lets SuperAdmin view/edit the
/// three fixed subscription tiers (edit only) and manage SMS packages (full CRUD). Liftdesk credentials
/// (base URL + Bearer key) stay server-side, resolved from the Liftdesk <see cref="Domain.Entities.Project"/>
/// row — the browser only ever talks to this CRM API with its normal JWT.
///
/// GET    /api/v1/pricing/plans                 — list plans (incl. inactive)
/// PUT    /api/v1/pricing/plans/{id}            — update a plan
/// GET    /api/v1/pricing/sms-packages          — list SMS packages (incl. inactive)
/// POST   /api/v1/pricing/sms-packages          — create an SMS package
/// PUT    /api/v1/pricing/sms-packages/{id}     — update an SMS package
/// DELETE /api/v1/pricing/sms-packages/{id}     — soft-delete (deactivate) an SMS package
/// </summary>
[Route("api/v1/pricing")]
[Authorize(Policy = "SuperAdmin")]
public sealed class PricingController : ApiControllerBase
{
    private readonly ILiftdeskPricingClient _pricingClient;
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<PricingController> _logger;

    /// <summary>Initialises a new instance of <see cref="PricingController"/>.</summary>
    public PricingController(
        ILiftdeskPricingClient pricingClient,
        IProjectRepository projectRepository,
        ILogger<PricingController> logger)
    {
        _pricingClient     = pricingClient;
        _projectRepository = projectRepository;
        _logger            = logger;
    }

    /// <summary>
    /// Resolves the Liftdesk base URL + API key from the first project that has both configured.
    /// Pricing is a global EMS concern, so any Liftdesk-connected project's credentials serve.
    /// </summary>
    private async Task<(string BaseUrl, string ApiKey)?> ResolveLiftdeskAsync(CancellationToken ct)
    {
        var projects = await _projectRepository.GetAllAsync(ct);
        var project = projects.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.LiftdeskBaseUrl) && !string.IsNullOrWhiteSpace(p.LiftdeskApiKey));

        return project is null ? null : (project.LiftdeskBaseUrl!, project.LiftdeskApiKey!);
    }

    /// <summary>Maps a Liftdesk envelope onto an HTTP response, preserving the upstream error status.</summary>
    private IActionResult FromEnvelope<T>(LiftdeskEnvelope<T> env)
    {
        if (env.Success)
            return OkResponse(env.Data);

        var msg = env.Message ?? env.Errors?.FirstOrDefault() ?? "Liftdesk fiyat servisi hatası.";

        // Remap an UPSTREAM auth failure (bad/stale Liftdesk key) to 502 so the browser's own
        // 401-interceptor doesn't mistake it for CRM session expiry and fire a spurious token refresh.
        // The human-readable message still travels in errors[], so the operator sees the real cause.
        var code = env.StatusCode is 401 or 403
            ? StatusCodes.Status502BadGateway
            : env.StatusCode is >= 400 and < 600 ? env.StatusCode : 400;

        return StatusCode(code, ApiResponse<object>.Fail(msg, code));
    }

    private IActionResult NotConfigured() =>
        BadRequest(ApiResponse<object>.Fail(
            "Liftdesk projesi yapılandırılmamış (Base URL + API Key). Proje Yönetimi → Liftdesk → API Ayarları'ndan girin.", 400));

    // ── Subscription plans ────────────────────────────────────────────────────

    /// <summary>Lists all subscription plans, including inactive ones.</summary>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(ApiResponse<List<LiftdeskPricingPlan>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken = default)
    {
        var creds = await ResolveLiftdeskAsync(cancellationToken);
        if (creds is null) return NotConfigured();

        var env = await _pricingClient.GetPlansAsync(creds.Value.BaseUrl, creds.Value.ApiKey, cancellationToken);
        return env.Success ? OkResponse(env.Data ?? new List<LiftdeskPricingPlan>()) : FromEnvelope(env);
    }

    /// <summary>Updates a subscription plan (full replace; tier and iyzico codes are not editable).</summary>
    [HttpPut("plans/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskPricingPlan>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] UpdatePricingPlanRequest body, CancellationToken cancellationToken = default)
    {
        var validation = ValidatePlan(body);
        if (validation is not null) return BadRequest(ApiResponse<object>.Fail(validation, 400));

        var creds = await ResolveLiftdeskAsync(cancellationToken);
        if (creds is null) return NotConfigured();

        var env = await _pricingClient.UpdatePlanAsync(creds.Value.BaseUrl, creds.Value.ApiKey, id, body, cancellationToken);
        return FromEnvelope(env);
    }

    // ── SMS packages ──────────────────────────────────────────────────────────

    /// <summary>Lists all SMS packages, including inactive ones.</summary>
    [HttpGet("sms-packages")]
    [ProducesResponseType(typeof(ApiResponse<List<LiftdeskSmsPackage>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSmsPackages(CancellationToken cancellationToken = default)
    {
        var creds = await ResolveLiftdeskAsync(cancellationToken);
        if (creds is null) return NotConfigured();

        var env = await _pricingClient.GetSmsPackagesAsync(creds.Value.BaseUrl, creds.Value.ApiKey, cancellationToken);
        return env.Success ? OkResponse(env.Data ?? new List<LiftdeskSmsPackage>()) : FromEnvelope(env);
    }

    /// <summary>Creates a new SMS package.</summary>
    [HttpPost("sms-packages")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSmsPackage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSmsPackage([FromBody] CreateSmsPackageRequest body, CancellationToken cancellationToken = default)
    {
        var validation = ValidateSmsPackage(body.Name, body.SmsCount, body.Price);
        if (validation is not null) return BadRequest(ApiResponse<object>.Fail(validation, 400));

        var creds = await ResolveLiftdeskAsync(cancellationToken);
        if (creds is null) return NotConfigured();

        var env = await _pricingClient.CreateSmsPackageAsync(creds.Value.BaseUrl, creds.Value.ApiKey, body, cancellationToken);
        return FromEnvelope(env);
    }

    /// <summary>Updates an SMS package.</summary>
    [HttpPut("sms-packages/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LiftdeskSmsPackage>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSmsPackage(Guid id, [FromBody] UpdateSmsPackageRequest body, CancellationToken cancellationToken = default)
    {
        var validation = ValidateSmsPackage(body.Name, body.SmsCount, body.Price);
        if (validation is not null) return BadRequest(ApiResponse<object>.Fail(validation, 400));

        var creds = await ResolveLiftdeskAsync(cancellationToken);
        if (creds is null) return NotConfigured();

        var env = await _pricingClient.UpdateSmsPackageAsync(creds.Value.BaseUrl, creds.Value.ApiKey, id, body, cancellationToken);
        return FromEnvelope(env);
    }

    /// <summary>Soft-deletes (deactivates) an SMS package.</summary>
    [HttpDelete("sms-packages/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteSmsPackage(Guid id, CancellationToken cancellationToken = default)
    {
        var creds = await ResolveLiftdeskAsync(cancellationToken);
        if (creds is null) return NotConfigured();

        var env = await _pricingClient.DeleteSmsPackageAsync(creds.Value.BaseUrl, creds.Value.ApiKey, id, cancellationToken);
        return env.Success ? OkResponse<object>(new { }, "Paket satıştan kaldırıldı.") : FromEnvelope(env);
    }

    // ── Client-side validation (mirrors the Liftdesk contract to fail fast) ─────

    private static string? ValidatePlan(UpdatePricingPlanRequest b)
    {
        if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 200)
            return "Plan adı zorunludur (en fazla 200 karakter).";
        if (b.PriceMonthly <= 0 || b.PriceYearly <= 0)
            return "Aylık ve yıllık fiyat 0'dan büyük olmalıdır.";
        if (b.MaxUsers < 0 || b.MaxElevators < 0)
            return "Kullanıcı/asansör limiti negatif olamaz (0 = sınırsız).";
        return null;
    }

    private static string? ValidateSmsPackage(string name, int smsCount, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return "Paket adı zorunludur (en fazla 200 karakter).";
        if (smsCount <= 0)
            return "SMS adedi 0'dan büyük olmalıdır.";
        if (price <= 0)
            return "Fiyat 0'dan büyük olmalıdır.";
        return null;
    }
}
