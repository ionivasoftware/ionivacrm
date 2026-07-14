using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.VendorInvoices;

/// <summary>
/// Default implementation of <see cref="IVendorInvoiceService"/>.
/// All amounts are compared with a 0.01 tolerance (see <see cref="AmountTolerance"/>).
/// </summary>
public sealed class VendorInvoiceService : IVendorInvoiceService
{
    /// <summary>Amounts within this absolute tolerance are treated as equal.</summary>
    private const decimal AmountTolerance = 0.01m;

    private readonly IVendorInvoiceRepository _repository;
    private readonly ILogger<VendorInvoiceService> _logger;

    /// <summary>Initialises a new instance of <see cref="VendorInvoiceService"/>.</summary>
    public VendorInvoiceService(IVendorInvoiceRepository repository, ILogger<VendorInvoiceService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<VendorInvoiceDto>> ExpectAsync(ExpectRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryNormalise(request.Provider, request.Year, request.Month, out var provider, out var error))
            return Result<VendorInvoiceDto>.Failure(error);

        var existing = await _repository.GetByPeriodAsync(provider, request.Year, request.Month, cancellationToken);

        if (existing is null)
        {
            var entity = new VendorInvoice
            {
                Provider       = provider,
                PeriodYear     = request.Year,
                PeriodMonth    = request.Month,
                BillingType    = request.BillingType ?? DefaultBillingType(provider),
                Status         = VendorInvoiceStatus.Expected,
                ExpectedAmount = request.ExpectedAmount,
                Currency       = request.Currency,
                DueDay         = request.DueDay ?? DefaultDueDay(provider),
                ExpectedOn     = DateTime.UtcNow,
            };
            var created = await _repository.AddAsync(entity, cancellationToken);
            _logger.LogInformation("VendorInvoice Expected created: {Provider} {Year}-{Month:D2}.", provider, request.Year, request.Month);
            return Result<VendorInvoiceDto>.Success(created.ToDto());
        }

        // Idempotent refresh — never downgrade a Received/Reconciled/Mismatch record back to Expected.
        if (request.ExpectedAmount.HasValue) existing.ExpectedAmount = request.ExpectedAmount;
        if (!string.IsNullOrWhiteSpace(request.Currency)) existing.Currency = request.Currency;
        if (request.DueDay.HasValue) existing.DueDay = request.DueDay.Value;
        if (request.BillingType.HasValue) existing.BillingType = request.BillingType.Value;

        // If a fresh expected amount arrived for an already-received record, re-run reconciliation.
        if (existing.Status is VendorInvoiceStatus.Received or VendorInvoiceStatus.Reconciled or VendorInvoiceStatus.Mismatch)
            ApplyReconciliation(existing);
        else if (existing.ExpectedOn is null)
            existing.ExpectedOn = DateTime.UtcNow;

        await _repository.UpdateAsync(existing, cancellationToken);
        return Result<VendorInvoiceDto>.Success(existing.ToDto());
    }

    /// <inheritdoc />
    public async Task<Result<VendorInvoiceDto>> MarkReceivedAsync(MarkReceivedRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryNormalise(request.Provider, request.Year, request.Month, out var provider, out var error))
            return Result<VendorInvoiceDto>.Failure(error);

        var entity = await _repository.GetByPeriodAsync(provider, request.Year, request.Month, cancellationToken);
        var isNew = entity is null;

        entity ??= new VendorInvoice
        {
            Provider    = provider,
            PeriodYear  = request.Year,
            PeriodMonth = request.Month,
            BillingType = DefaultBillingType(provider),
            DueDay      = DefaultDueDay(provider),
            ExpectedOn  = DateTime.UtcNow,
        };

        if (request.ReceivedAmount.HasValue) entity.ReceivedAmount = request.ReceivedAmount;
        if (!string.IsNullOrWhiteSpace(request.Currency)) entity.Currency = request.Currency;
        if (!string.IsNullOrWhiteSpace(request.InvoiceNumber)) entity.InvoiceNumber = request.InvoiceNumber;
        if (!string.IsNullOrWhiteSpace(request.PdfUrl)) entity.PdfUrl = request.PdfUrl;

        entity.ReceivedOn = DateTime.UtcNow;
        entity.AlertedOn  = null; // an arrived invoice clears any prior Missing alarm

        ApplyReconciliation(entity);

        if (isNew)
            await _repository.AddAsync(entity, cancellationToken);
        else
            await _repository.UpdateAsync(entity, cancellationToken);

        _logger.LogInformation("VendorInvoice MarkReceived: {Provider} {Year}-{Month:D2} → {Status}.",
            provider, request.Year, request.Month, entity.Status);
        return Result<VendorInvoiceDto>.Success(entity.ToDto());
    }

    /// <inheritdoc />
    public async Task<Result<List<VendorInvoiceDto>>> SeedMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (month is < 1 or > 12)
            return Result<List<VendorInvoiceDto>>.Failure("Ay 1–12 aralığında olmalı.");

        var results = new List<VendorInvoiceDto>();
        foreach (var p in KnownProviders.All)
        {
            var res = await ExpectAsync(
                new ExpectRequest(p.Key, year, month, DueDay: p.DueDay, BillingType: p.BillingType),
                cancellationToken);
            if (res.IsSuccess && res.Value is not null)
                results.Add(res.Value);
        }

        _logger.LogInformation("VendorInvoice SeedMonth {Year}-{Month:D2}: {Count} baseline satırı.", year, month, results.Count);
        return Result<List<VendorInvoiceDto>>.Success(results);
    }

    /// <inheritdoc />
    public async Task<Result<ReconcileResult>> ReconcileAsync(DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var now = (asOf ?? DateTime.UtcNow);
        if (now.Kind == DateTimeKind.Unspecified)
            now = DateTime.SpecifyKind(now, DateTimeKind.Utc);

        var expected = await _repository.GetExpectedAsync(cancellationToken);
        var missing = new List<VendorInvoiceDto>();

        foreach (var inv in expected)
        {
            if (now <= inv.DueDate()) continue;

            inv.Status    = VendorInvoiceStatus.Missing;
            inv.AlertedOn = DateTime.UtcNow;
            await _repository.UpdateAsync(inv, cancellationToken);
            missing.Add(inv.ToDto());

            _logger.LogWarning(
                "VendorInvoice MISSING: {Provider} {Year}-{Month:D2} — due {Due:yyyy-MM-dd}, no invoice received.",
                inv.Provider, inv.PeriodYear, inv.PeriodMonth, inv.DueDate());
        }

        // Extension point (Phase 1 alarm): the Missing list is surfaced as a red badge in the CRM and
        // logged as warnings here. Wire Slack/e-mail notification off `missing` when required.
        if (missing.Count > 0)
            _logger.LogWarning("VendorInvoice reconcile: {Count} eksik fatura tespit edildi.", missing.Count);

        return Result<ReconcileResult>.Success(new ReconcileResult(missing.Count, missing));
    }

    /// <inheritdoc />
    public async Task<Result<List<VendorInvoiceDto>>> ListAsync(
        int? year = null, int? month = null, VendorInvoiceStatus? status = null, string? provider = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListAsync(year, month, status, provider?.Trim(), cancellationToken);
        return Result<List<VendorInvoiceDto>>.Success(items.Select(i => i.ToDto()).ToList());
    }

    /// <inheritdoc />
    public Task<int> CountMissingAsync(CancellationToken cancellationToken = default)
        => _repository.CountMissingAsync(cancellationToken);

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the status by comparing amounts: both present → Reconciled/Mismatch (within tolerance);
    /// received-only → Received.
    /// </summary>
    private static void ApplyReconciliation(VendorInvoice inv)
    {
        if (inv.ExpectedAmount.HasValue && inv.ReceivedAmount.HasValue)
        {
            var diff = Math.Abs(inv.ExpectedAmount.Value - inv.ReceivedAmount.Value);
            inv.Status = diff <= AmountTolerance ? VendorInvoiceStatus.Reconciled : VendorInvoiceStatus.Mismatch;
        }
        else
        {
            inv.Status = VendorInvoiceStatus.Received;
        }
    }

    private static bool TryNormalise(string? provider, int year, int month, out string normalised, out string error)
    {
        normalised = provider?.Trim() ?? string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(normalised)) { error = "Sağlayıcı (provider) zorunlu."; return false; }
        if (normalised.Length > 50)               { error = "Sağlayıcı adı 50 karakteri aşamaz."; return false; }
        if (year is < 2000 or > 2100)             { error = "Geçersiz yıl."; return false; }
        if (month is < 1 or > 12)                 { error = "Ay 1–12 aralığında olmalı."; return false; }
        return true;
    }

    private static VendorBillingType DefaultBillingType(string provider) =>
        KnownProviders.All.FirstOrDefault(p => string.Equals(p.Key, provider, StringComparison.OrdinalIgnoreCase))?.BillingType
        ?? VendorBillingType.Usage;

    private static int DefaultDueDay(string provider) =>
        KnownProviders.All.FirstOrDefault(p => string.Equals(p.Key, provider, StringComparison.OrdinalIgnoreCase))?.DueDay
        ?? 7;
}
