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
                // 1. Resolve customer (load fresh — needed for company name in invoice title)
                var customer = await _customerRepository.GetByIdAsync(contract.CustomerId, cancellationToken);
                if (customer is null)
                {
                    var msg = $"Contract {contract.Id}: customer {contract.CustomerId} bulunamadı.";
                    errors.Add(msg);
                    _logger.LogWarning("Contract invoice sync SKIP: {Reason}", msg);
                    skipped++;
                    continue;
                }

                // 2. Resolve "RezervAl Aylık Lisans Bedeli" product mapping for the project
                if (!productCache.TryGetValue(contract.ProjectId, out var product))
                {
                    product = await _productRepository.GetByNameAsync(
                        contract.ProjectId, RezervalMonthlyProductName, cancellationToken);
                    productCache[contract.ProjectId] = product;
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

                var nextDate = contract.NextInvoiceDate!.Value;

                // 4. Dedup key — reuses the EmsPaymentId column as a generic external ref
                var dedupKey = $"CONTRACT-{contract.Id}-{nextDate:yyyyMM}";
                if (await _invoiceRepository.ExistsByEmsPaymentIdAsync(dedupKey, cancellationToken))
                {
                    skipped++;
                    // Still advance NextInvoiceDate so we don't loop forever on already-created invoices.
                    AdvanceContractDate(contract);
                    if (ContractFullyConsumed(contract))
                    {
                        contract.Status = ContractStatus.Completed;
                        contract.NextInvoiceDate = null;
                        contractsCompleted++;
                    }
                    await _contractRepository.UpdateAsync(contract, cancellationToken);
                    continue;
                }

                // 5. Build invoice line — quantity 1 × MonthlyAmount, vatRate from product
                int vatRate = (int)(product.TaxRate * 100);
                decimal unitPrice = contract.MonthlyAmount;
                decimal netTotal = unitPrice;
                decimal grossTotal = unitPrice * (1 + vatRate / 100m);

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

                // 6. Persist draft invoice
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

                // 7. Advance contract dates and mark Completed if past EndDate
                contract.LastInvoiceGeneratedDate = nextDate;
                AdvanceContractDate(contract);

                if (ContractFullyConsumed(contract))
                {
                    contract.Status = ContractStatus.Completed;
                    contract.NextInvoiceDate = null;
                    contractsCompleted++;
                }

                await _contractRepository.UpdateAsync(contract, cancellationToken);

                // 8. Sync log entry
                await _syncLogRepository.AddAsync(new SyncLog
                {
                    Id          = Guid.NewGuid(),
                    ProjectId   = contract.ProjectId,
                    Source      = SyncSource.SaasB,
                    Direction   = SyncDirection.Inbound,
                    EntityType  = "ContractInvoice",
                    EntityId    = dedupKey,
                    Status      = SyncStatus.Success,
                    SyncedAt    = DateTime.UtcNow,
                    Payload     = JsonSerializer.Serialize(new
                    {
                        contractId = contract.Id,
                        customerId = contract.CustomerId,
                        billingMonth = nextDate.ToString("yyyy-MM"),
                        invoiceId = invoice.Id,
                        amount = unitPrice
                    })
                }, cancellationToken);

                _logger.LogInformation(
                    "Contract invoice sync: created draft {InvoiceId} for contract {ContractId} ({Month}).",
                    invoice.Id, contract.Id, nextDate.ToString("yyyy-MM"));
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

        // Dump every error so we can see the actual skip reason in Railway logs,
        // not just the count.
        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Contract invoice sync errors ({Count}):\n{Errors}",
                errors.Count, string.Join("\n", errors));
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
