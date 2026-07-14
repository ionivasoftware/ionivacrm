using IonCrm.API.Common;
using IonCrm.Application.Features.VendorInvoices;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using IonCrm.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// SuperAdmin-only vendor-invoice reconciliation (company operational costs: Anthropic, Railway,
/// Google Cloud, Google Workspace). Phase 1 — reconcile + alarm skeleton.
///
/// GET    /api/v1/vendor-invoices                 — list (year/month/status/provider filters)
/// GET    /api/v1/vendor-invoices/missing-count   — count of Missing (red badge)
/// POST   /api/v1/vendor-invoices/expect          — idempotent upsert of an expected bill
/// POST   /api/v1/vendor-invoices/mark-received   — record a received PDF invoice
/// POST   /api/v1/vendor-invoices/seed-month      — seed baseline rows for known providers
/// POST   /api/v1/vendor-invoices/reconcile       — flip overdue Expected → Missing (also runs daily)
/// </summary>
[Route("api/v1/vendor-invoices")]
[Authorize(Policy = "SuperAdmin")]
public sealed class VendorInvoicesController : ApiControllerBase
{
    private readonly IVendorInvoiceService _service;
    private readonly ICostAutoExpectService _autoExpect;

    /// <summary>Initialises a new instance of <see cref="VendorInvoicesController"/>.</summary>
    public VendorInvoicesController(IVendorInvoiceService service, ICostAutoExpectService autoExpect)
    {
        _service = service;
        _autoExpect = autoExpect;
    }

    /// <summary>Lists reconciliation records. All filters optional.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<VendorInvoiceDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] VendorInvoiceStatus? status = null,
        [FromQuery] string? provider = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ListAsync(year, month, status, provider, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Returns the number of records currently in Missing status (for the alarm badge).</summary>
    [HttpGet("missing-count")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MissingCount(CancellationToken cancellationToken = default)
    {
        var count = await _service.CountMissingAsync(cancellationToken);
        return OkResponse<object>(new { count });
    }

    /// <summary>Idempotent upsert of an expected bill (from a cost API or a fixed subscription).</summary>
    [HttpPost("expect")]
    [ProducesResponseType(typeof(ApiResponse<VendorInvoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Expect([FromBody] ExpectRequest body, CancellationToken cancellationToken = default)
    {
        var result = await _service.ExpectAsync(body, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Records a received PDF invoice; reconciles against the expected amount when both are known.</summary>
    [HttpPost("mark-received")]
    [ProducesResponseType(typeof(ApiResponse<VendorInvoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MarkReceived([FromBody] MarkReceivedRequest body, CancellationToken cancellationToken = default)
    {
        var result = await _service.MarkReceivedAsync(body, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Seeds baseline Expected rows for all known providers for a period (idempotent).</summary>
    [HttpPost("seed-month")]
    [ProducesResponseType(typeof(ApiResponse<List<VendorInvoiceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SeedMonth([FromBody] SeedMonthRequest body, CancellationToken cancellationToken = default)
    {
        var result = await _service.SeedMonthAsync(body.Year, body.Month, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Runs the reconcile sweep now (overdue Expected → Missing). Returns the Missing list.</summary>
    [HttpPost("reconcile")]
    [ProducesResponseType(typeof(ApiResponse<ReconcileResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile([FromBody] ReconcileRequest? body = null, CancellationToken cancellationToken = default)
    {
        var result = await _service.ReconcileAsync(body?.AsOf, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>
    /// Pulls each configured provider's cost for a period and upserts Expected rows (Phase 2).
    /// Anthropic auto-fetches from the Admin Cost API; other providers use their configured fixed amount.
    /// </summary>
    [HttpPost("auto-expect")]
    [ProducesResponseType(typeof(ApiResponse<AutoExpectSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AutoExpect([FromBody] SeedMonthRequest body, CancellationToken cancellationToken = default)
    {
        var result = await _autoExpect.RunAsync(body.Year, body.Month, cancellationToken);
        return ResultToResponse(result);
    }
}

/// <summary>Request body for POST /seed-month.</summary>
public record SeedMonthRequest(int Year, int Month);

/// <summary>Request body for POST /reconcile. <c>AsOf</c> defaults to now when omitted.</summary>
public record ReconcileRequest(DateTime? AsOf);
