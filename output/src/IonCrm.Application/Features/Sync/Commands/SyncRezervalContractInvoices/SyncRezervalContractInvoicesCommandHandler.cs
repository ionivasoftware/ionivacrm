using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace IonCrm.Application.Features.Sync.Commands.SyncRezervalContractInvoices;

/// <summary>Handles <see cref="SyncRezervalContractInvoicesCommand"/>.</summary>
public sealed class SyncRezervalContractInvoicesCommandHandler
    : IRequestHandler<SyncRezervalContractInvoicesCommand, Result<SyncRezervalContractInvoicesResult>>
{
    /// <summary>Fixed product name used for the recurring RezervAl monthly licence line item.</summary>
    public const string RezervalMonthlyProductName = "RezervAl Aylık Lisans Bedeli";

    private readonly ICustomerContractRepository _contractRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IParasutProductRepository _productRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly IParasutService _parasutService;
    private readonly ILogger<SyncRezervalContractInvoicesCommandHandler> _logger;

    /// <summary>Initialises a new instance.</summary>
    public SyncRezervalContractInvoicesCommandHandler(
        ICustomerContractRepository contractRepository,
        ICustomerRepository customerRepository,
        IParasutProductRepository productRepository,
        IInvoiceRepository invoiceRepository,
        ISyncLogRepository syncLogRepository,
        IParasutService parasutService,
        ILogger<SyncRezervalContractInvoicesCommandHandler> logger)
    {
        _contractRepository = contractRepository;
        _customerRepository = customerRepository;
        _productRepository  = productRepository;
        _invoiceRepository  = invoiceRepository;
        _syncLogRepository  = syncLogRepository;
        _parasutService     = parasutService;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SyncRezervalContractInvoicesResult>> Handle(
        SyncRezervalContractInvoicesCommand request,
        CancellationToken cancellationToken)
    {
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

        var contracts = await _contractRepository.GetActiveEftContractsDueAsync(today, cancellationToken);

        int contractsScanned   = 0;
        int invoicesCreated    = 0;
        int skipped            = 0;
        int contractsCompleted = 0;
        var errors             = new List<string>();

        // Cache product lookups per project so we don't re-query in tight loops.
        var productCache = new Dictionary<Guid, ParasutProduct?>();

        foreach (var contract in contracts)
        {
            contractsScanned++;

            try
            {
                // 1. Resolve customer (load fresh — needed for company name in invoice title).
                // IMPORTANT: Use the tenant-bypass lookup. Background jobs run with no HTTP
                // context → ICurrentUserService.ProjectIds is empty → the global query filter
                // on Customer would hide every row, even though the contract row found above
                // (via the contract repo's IgnoreQueryFilters path) clearly proves the
                // customer exists. The plain GetByIdAsync would return null here.
                var customer = await _customerRepository.GetByIdIgnoringTenantAsync(contract.CustomerId, cancellationToken);
                if (customer is null)
                {
                    var msg = $"Contract {contract.Id}: customer {contract.CustomerId} bulunamadı.";
                    errors.Add(msg);
                    _logger.LogWarning("Contract invoice sync SKIP: {Reason}", msg);
                    skipped++;
                    continue;
                }

                // 2. Resolve "RezervAl Aylık Lisans Bedeli" product mapping (global — one
                //    catalog shared by every project, so this lookup is project-independent).
                //    Cache by a single sentinel key since there is only one global mapping.
                if (!productCache.TryGetValue(Guid.Empty, out var product))
                {
                    product = await _productRepository.GetByNameAsync(
                        RezervalMonthlyProductName, cancellationToken);
                    productCache[Guid.Empty] = product;
                }

                if (product is null || string.IsNullOrEmpty(product.ParasutProductId))
                {
                    var msg = $"Contract {contract.Id} (project {contract.ProjectId}): " +
                              $"'{RezervalMonthlyProductName}' Paraşüt ürün eşleştirmesi yok " +
                              $"(product={(product is null ? "null" : "found but ParasutProductId empty")}).";
                    errors.Add(msg);
                    _logger.LogWarning("Contract invoice sync SKIP: {Reason}", msg);
                    skipped++;
                    continue;
                }

                // 3. Auto-enrich product data from Paraşüt API if incomplete
                if (string.IsNullOrEmpty(product.ParasutProductName)
                    || product.TaxRate == 0
                    || product.UnitPrice == 0)
                {
                    var (parasutData, _) = await _parasutService.GetProductByIdAsync(
                        contract.ProjectId, product.ParasutProductId, cancellationToken);

                    if (parasutData?.Data?.Attributes is { } attrs)
                    {
                        if (string.IsNullOrEmpty(product.ParasutProductName))
                            product.ParasutProductName = attrs.Name;

                        if (product.TaxRate == 0 && attrs.VatRateInt is { } vr)
                            product.TaxRate = vr / 100m;

                        if (product.UnitPrice == 0)
                        {
                            var priceStr = attrs.SalesPrice ?? attrs.ListPrice ?? attrs.SalesPriceInTrl;
                            if (decimal.TryParse(priceStr, NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out var p))
                                product.UnitPrice = p;
                        }

                        await _productRepository.UpdateAsync(product, cancellationToken);
                    }
                }

                // 4. Pre-compute invoice line fields (same for every month of this contract).
                int vatRate = (int)Math.Round(product.TaxRate * 100);
                if (vatRate <= 0)
                {
                    _logger.LogWarning(
                        "Contract invoice sync: product '{Name}' has no TaxRate; defaulting to %20 for contract {ContractId}.",
                        product.ProductName, contract.Id);
                    vatRate = 20;
                }

                decimal grossTotal = contract.MonthlyAmount;
                decimal netTotal   = Math.Round(grossTotal / (1 + vatRate / 100m), 2);
                decimal unitPrice  = netTotal;

                // 5. Process ALL due months in a single cycle. A backdated contract (e.g.
                //    start = Jan, today = May) would otherwise need 4 × 15-min sync cycles
                //    to catch up one month at a time. The inner while handles all of them
                //    in one pass, creating one draft invoice per month.
                while (contract.NextInvoiceDate.HasValue && contract.NextInvoiceDate.Value <= today)
                {
                    var nextDate = contract.NextInvoiceDate.Value;

                    // Dedup key — reuses the EmsPaymentId column as a generic external ref
                    var dedupKey = $"CONTRACT-{contract.Id}-{nextDate:yyyyMM}";
                    if (await _invoiceRepository.ExistsByEmsPaymentIdAsync(dedupKey, cancellationToken))
                    {
                        skipped++;
                        AdvanceContractDate(contract);
                        if (ContractFullyConsumed(contract))
                        {
                            contract.Status = ContractStatus.Completed;
                            contract.NextInvoiceDate = null;
                            contractsCompleted++;
                        }
                        continue; // next month in the while loop
                    }

                    var lines = new[]
                    {
                        new
                        {
                            description = !string.IsNullOrEmpty(product.ParasutProductName)
                                              ? product.ParasutProductName
                                              : RezervalMonthlyProductName,
                            quantity           = 1,
                            unitPrice,
                            vatRate,
                            discountValue      = 0m,
                            discountType       = "percentage",
                            unit               = "Adet",
                            parasutProductId   = product.ParasutProductId,
                            parasutProductName = product.ParasutProductName
                        }
                    };

                    var invoice = new Invoice
                    {
                        ProjectId    = contract.ProjectId,
                        CustomerId   = contract.CustomerId,
                        Title        = $"{customer.CompanyName} - {nextDate:MMMM yyyy} Abonelik",
                        Description  = null,
                        IssueDate    = nextDate,
                        DueDate      = nextDate.AddDays(30),
                        Currency     = "TRL",
                        GrossTotal   = grossTotal,
                        NetTotal     = netTotal,
                        LinesJson    = JsonSerializer.Serialize(lines),
                        Status       = InvoiceStatus.Draft,
                        EmsPaymentId = dedupKey
                    };

                    await _invoiceRepository.AddAsync(invoice, cancellationToken);
                    invoicesCreated++;

                    contract.LastInvoiceGeneratedDate = nextDate;
                    AdvanceContractDate(contract);

                    if (ContractFullyConsumed(contract))
                    {
                        contract.Status = ContractStatus.Completed;
                        contract.NextInvoiceDate = null;
                        contractsCompleted++;
                    }
                }

                // Persist contract state after processing all due months.
                await _contractRepository.UpdateAsync(contract, cancellationToken);

                _logger.LogInformation(
                    "Contract invoice sync: processed contract {ContractId} — created {Created} invoice(s), skipped {Skipped}.",
                    contract.Id, invoicesCreated, skipped);
            }
            catch (Exception ex)
            {
                var msg = $"Contract {contract.Id}: {ex.Message}";
                errors.Add(msg);
                _logger.LogError(ex, "Contract invoice sync failed for contract {ContractId}.", contract.Id);
            }
        }

        var result = new SyncRezervalContractInvoicesResult(
            contractsScanned, invoicesCreated, skipped, contractsCompleted, errors);

        _logger.LogInformation(
            "Contract invoice sync complete. Scanned={Scanned} Created={Created} Skipped={Skipped} Completed={Completed} Errors={Errors}.",
            contractsScanned, invoicesCreated, skipped, contractsCompleted, errors.Count);

        // Dump every error on its own log line — Railway's log sink truncates
        // multi-line messages after the first '\n', so a bulk dump with embedded
        // newlines shows up empty in the viewer.
        for (int i = 0; i < errors.Count; i++)
        {
            _logger.LogWarning(
                "Contract invoice sync error #{Index}/{Total}: {Error}",
                i + 1, errors.Count, errors[i]);
        }

        return Result<SyncRezervalContractInvoicesResult>.Success(result);
    }

    /// <summary>
    /// Advances <see cref="CustomerContract.NextInvoiceDate"/> by one month, anchoring on the
    /// contract's <see cref="CustomerContract.StartDate"/> day-of-month.  When the target month
    /// has fewer days than the start day (e.g. start=31 March → April has 30), the date falls
    /// back to the last day of the target month.
    /// </summary>
    private static void AdvanceContractDate(CustomerContract contract)
    {
        if (!contract.NextInvoiceDate.HasValue) return;

        var current = contract.NextInvoiceDate.Value;
        var anchorDay = contract.StartDate.Day;

        // Move to the next month, then clamp the day-of-month so we never overflow into a later month.
        var nextMonth = current.AddMonths(1);
        int daysInNextMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
        int targetDay = Math.Min(anchorDay, daysInNextMonth);

        contract.NextInvoiceDate = DateTime.SpecifyKind(
            new DateTime(nextMonth.Year, nextMonth.Month, targetDay),
            DateTimeKind.Utc);
    }

    /// <summary>
    /// Returns true when the contract has a non-null <see cref="CustomerContract.EndDate"/>
    /// and the just-advanced <see cref="CustomerContract.NextInvoiceDate"/> is past it.
    /// </summary>
    private static bool ContractFullyConsumed(CustomerContract contract) =>
        contract.EndDate.HasValue
        && contract.NextInvoiceDate.HasValue
        && contract.NextInvoiceDate.Value > contract.EndDate.Value;
}
