using IonCrm.API.Common;
using IonCrm.Application.Features.VendorInvoices;
using IonCrm.Application.Features.VendorInvoices.CostProviders;
using IonCrm.Application.Features.VendorInvoices.EmailCollector;
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
[Authorize(Policy = "VendorInvoiceAccess")]
public sealed class VendorInvoicesController : ApiControllerBase
{
    private readonly IVendorInvoiceService _service;
    private readonly ICostAutoExpectService _autoExpect;
    private readonly IInvoiceEmailCollector _emailCollector;

    /// <summary>Initialises a new instance of <see cref="VendorInvoicesController"/>.</summary>
    public VendorInvoicesController(
        IVendorInvoiceService service,
        ICostAutoExpectService autoExpect,
        IInvoiceEmailCollector emailCollector)
    {
        _service = service;
        _autoExpect = autoExpect;
        _emailCollector = emailCollector;
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

    /// <summary>
    /// Scans the accounting mailbox for vendor invoice e-mails and records the received figures (Phase 3).
    /// Pass <c>dryRun=true</c> to preview matches without writing.
    /// </summary>
    [HttpPost("collect-emails")]
    [ProducesResponseType(typeof(ApiResponse<EmailCollectSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CollectEmails([FromQuery] bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var result = await _emailCollector.CollectAsync(dryRun, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Soft-deletes a reconciliation record.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return ResultToResponse(result);
    }

    /// <summary>Returns the stored PDF file for an invoice (inline), or 404 when none exists.</summary>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken cancellationToken = default)
    {
        var pdf = await _service.GetPdfAsync(id, cancellationToken);
        if (pdf is null)
            return NotFound(ApiResponse<object>.Fail("Bu fatura için kayıtlı PDF yok.", 404));

        var name = string.IsNullOrWhiteSpace(pdf.FileName) ? $"fatura-{id}.pdf" : pdf.FileName;
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{name}\"";
        return File(pdf.Content, string.IsNullOrWhiteSpace(pdf.ContentType) ? "application/pdf" : pdf.ContentType);
    }

    /// <summary>Uploads (or replaces) the PDF file for an invoice via multipart form-data.</summary>
    [HttpPost("{id:guid}/pdf")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPdf(Guid id, IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Dosya boş.", 400));

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/pdf" : file.ContentType;

        var result = await _service.SavePdfAsync(id, file.FileName, contentType, ms.ToArray(), cancellationToken);
        return ResultToResponse(result);
    }
}

/// <summary>Request body for POST /seed-month.</summary>
public record SeedMonthRequest(int Year, int Month);

/// <summary>Request body for POST /reconcile. <c>AsOf</c> defaults to now when omitted.</summary>
public record ReconcileRequest(DateTime? AsOf);
