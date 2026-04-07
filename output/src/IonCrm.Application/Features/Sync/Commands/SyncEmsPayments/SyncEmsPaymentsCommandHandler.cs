using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Application.Features.Sync.Commands.SyncEmsPayments;

/// <summary>Handles <see cref="SyncEmsPaymentsCommand"/>.</summary>
public sealed class SyncEmsPaymentsCommandHandler
    : IRequestHandler<SyncEmsPaymentsCommand, Result<SyncEmsPaymentsResult>>
{
    private readonly IProjectRepository _projectRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IParasutProductRepository _productRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly IParasutService _parasutService;
    private readonly ILogger<SyncEmsPaymentsCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="SyncEmsPaymentsCommandHandler"/>.</summary>
    public SyncEmsPaymentsCommandHandler(
        IProjectRepository projectRepository,
        ICustomerRepository customerRepository,
        IParasutProductRepository productRepository,
        IInvoiceRepository invoiceRepository,
        ISyncLogRepository syncLogRepository,
        ISaasAClient saasAClient,
        IParasutService parasutService,
        ILogger<SyncEmsPaymentsCommandHandler> logger)
    {
        _projectRepository  = projectRepository;
        _customerRepository = customerRepository;
        _productRepository  = productRepository;
        _invoiceRepository  = invoiceRepository;
        _syncLogRepository  = syncLogRepository;
        _saasAClient        = saasAClient;
        _parasutService     = parasutService;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SyncEmsPaymentsResult>> Handle(
        SyncEmsPaymentsCommand request,
        CancellationToken cancellationToken)
    {
        var projects       = await _projectRepository.GetAllAsync(cancellationToken);
        var emsProjects    = projects.Where(p => !string.IsNullOrWhiteSpace(p.EmsApiKey)).ToList();

        int projectsScanned  = 0;
        int paymentsFetched  = 0;
        int invoicesCreated  = 0;
        int skipped          = 0;
        var errors           = new List<string>();

        foreach (var project in emsProjects)
        {
            // Summary log written at the start of each project scan — visible even when 0 payments
            var summaryLog = new SyncLog
            {
                Id          = Guid.NewGuid(),
                ProjectId   = project.Id,
                Source      = SyncSource.SaasA,
                Direction   = SyncDirection.Inbound,
                EntityType  = "PaymentSync",
                Status      = SyncStatus.Pending,
            };
            await _syncLogRepository.AddAsync(summaryLog, cancellationToken);

            try
            {
                var response = await _saasAClient.GetRecentPaymentsAsync(
                    project.EmsApiKey,
                    request.WindowMinutes,
                    cancellationToken,
                    project.EmsBaseUrl);

                projectsScanned++;
                paymentsFetched += response.Data.Count;

                _logger.LogDebug(
                    "EMS payment sync: project {ProjectId} returned {Count} payments (window={Window}min).",
                    project.Id, response.Data.Count, request.WindowMinutes);

                foreach (var payment in response.Data)
                {
                    var emsPaymentId = payment.Id.ToString();

                    // 1. Dedup — skip if already recorded
                    if (await _invoiceRepository.ExistsByEmsPaymentIdAsync(emsPaymentId, cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    // 2. Resolve customer by EMS companyId
                    //    LegacyId formats: "SAASA-{id}" or plain "{id}"
                    var customer =
                        await _customerRepository.GetByLegacyIdAsync($"SAASA-{payment.CompanyId}", cancellationToken)
                        ?? await _customerRepository.GetByLegacyIdAsync(payment.CompanyId.ToString(), cancellationToken);

                    if (customer is null)
                    {
                        _logger.LogWarning(
                            "EMS payment sync: no customer found for EMS companyId={CompanyId} (paymentId={PaymentId}). Skipping.",
                            payment.CompanyId, payment.Id);

                        await _syncLogRepository.AddAsync(new SyncLog
                        {
                            Id           = Guid.NewGuid(),
                            ProjectId    = project.Id,
                            Source       = SyncSource.SaasA,
                            Direction    = SyncDirection.Inbound,
                            EntityType   = "Payment",
                            EntityId     = emsPaymentId,
                            Status       = SyncStatus.Failed,
                            ErrorMessage = $"Müşteri bulunamadı — EMS companyId={payment.CompanyId}",
                            SyncedAt     = DateTime.UtcNow,
                        }, cancellationToken);

                        skipped++;
                        continue;
                    }

                    // 3. Resolve product mapping (optional)
                    string lineDescription;
                    decimal unitPrice;
                    int     vatRate;           // integer percentage: 20 = %20
                    string? parasutProductId   = null;
                    string? parasutProductName = null;

                    if (payment.ProductId.HasValue)
                    {
                        var product = await _productRepository.GetByEmsProductIdAsync(
                            project.Id,
                            payment.ProductId.Value.ToString(),
                            cancellationToken);

                        if (product is not null)
                        {
                            // If product data is incomplete, enrich from Paraşüt API
                            if (!string.IsNullOrEmpty(product.ParasutProductId) &&
                                (string.IsNullOrEmpty(product.ParasutProductName) || product.TaxRate == 0 || product.UnitPrice == 0))
                            {
                                var (parasutData, _) = await _parasutService.GetProductByIdAsync(
                                    project.Id, product.ParasutProductId, cancellationToken);

                                if (parasutData?.Data?.Attributes is { } attrs)
                                {
                                    if (string.IsNullOrEmpty(product.ParasutProductName))
                                        product.ParasutProductName = attrs.Name;

                                    if (product.TaxRate == 0 && attrs.VatRateInt is { } vr)
                                        product.TaxRate = vr / 100m;

                                    if (product.UnitPrice == 0)
                                    {
                                        var priceStr = attrs.SalesPrice ?? attrs.ListPrice ?? attrs.SalesPriceInTrl;
                                        if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any,
                                                System.Globalization.CultureInfo.InvariantCulture, out var p))
                                            product.UnitPrice = p;
                                    }

                                    await _productRepository.UpdateAsync(product, cancellationToken);
                                }
                            }

                            lineDescription    = !string.IsNullOrEmpty(product.ParasutProductName)
                                                    ? product.ParasutProductName
                                                    : product.ProductName;
                            unitPrice          = product.UnitPrice > 0 ? product.UnitPrice : payment.SubTotal;
                            vatRate            = (int)(product.TaxRate * 100);
                            parasutProductId   = product.ParasutProductId;
                            parasutProductName = product.ParasutProductName;
                        }
                        else
                        {
                            lineDescription = payment.ProductName ?? $"EMS Ürün #{payment.ProductId}";
                            unitPrice       = payment.SubTotal;
                            vatRate         = payment.SubTotal > 0
                                ? (int)Math.Round(payment.VatPrice / payment.SubTotal * 100)
                                : 20;
                        }
                    }
                    else
                    {
                        lineDescription = $"EMS Ödeme #{payment.Id}";
                        unitPrice       = payment.SubTotal;
                        vatRate         = payment.SubTotal > 0
                            ? (int)Math.Round(payment.VatPrice / payment.SubTotal * 100)
                            : 20;
                    }

                    // 4. Build invoice line JSON (parasutProductId included when mapping exists)
                    var lines = new[]
                    {
                        new
                        {
                            description      = lineDescription,
                            quantity         = 1,
                            unitPrice,
                            vatRate,
                            discountValue    = (decimal?)null,
                            discountType     = (string?)null,
                            unit             = (string?)null,
                            parasutProductId,
                            parasutProductName
                        }
                    };

                    // 5. Persist draft invoice
                    var invoice = new Invoice
                    {
                        ProjectId    = project.Id,
                        CustomerId   = customer.Id,
                        Title        = $"EMS Ödeme - {payment.PaymentType} ({payment.CreatedOn:dd.MM.yyyy})",
                        Description  = null,
                        IssueDate    = payment.CreatedOn,
                        DueDate      = payment.CreatedOn.Date,
                        Currency     = "TRL",
                        GrossTotal   = payment.Price,
                        NetTotal     = payment.SubTotal,
                        LinesJson    = JsonSerializer.Serialize(lines),
                        Status       = InvoiceStatus.Draft,
                        EmsPaymentId = emsPaymentId
                    };

                    await _invoiceRepository.AddAsync(invoice, cancellationToken);
                    invoicesCreated++;

                    // 6. Write sync log entry for this payment
                    await _syncLogRepository.AddAsync(new SyncLog
                    {
                        Id          = Guid.NewGuid(),
                        ProjectId   = project.Id,
                        Source      = SyncSource.SaasA,
                        Direction   = SyncDirection.Inbound,
                        EntityType  = "Payment",
                        EntityId    = emsPaymentId,
                        Status      = SyncStatus.Success,
                        SyncedAt    = DateTime.UtcNow,
                        Payload     = JsonSerializer.Serialize(new
                        {
                            payment.Id,
                            payment.CompanyId,
                            payment.ProductId,
                            payment.ProductName,
                            payment.PaymentType,
                            payment.Price,
                            payment.SubTotal,
                            invoiceId = invoice.Id
                        })
                    }, cancellationToken);

                    _logger.LogInformation(
                        "EMS payment sync: created invoice draft for customer {CustomerId} from EMS payment {PaymentId} (company {CompanyId}).",
                        customer.Id, payment.Id, payment.CompanyId);
                }

                // Update summary log with final result
                summaryLog.Status   = SyncStatus.Success;
                summaryLog.SyncedAt = DateTime.UtcNow;
                summaryLog.Payload  = $"payments={response.Data.Count} created={invoicesCreated} skipped={skipped}";
                await _syncLogRepository.UpdateAsync(summaryLog, cancellationToken);
            }
            catch (Exception ex)
            {
                var msg = $"Project {project.Id} ({project.Name}): {ex.Message}";
                errors.Add(msg);
                _logger.LogError(ex, "EMS payment sync failed for project {ProjectId}.", project.Id);

                summaryLog.Status       = SyncStatus.Failed;
                summaryLog.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                summaryLog.SyncedAt     = DateTime.UtcNow;
                await _syncLogRepository.UpdateAsync(summaryLog, cancellationToken);
            }
        }

        var result = new SyncEmsPaymentsResult(
            projectsScanned, paymentsFetched, invoicesCreated, skipped, errors);

        _logger.LogInformation(
            "EMS payment sync complete. Projects={Projects} Payments={Payments} Created={Created} Skipped={Skipped} Errors={Errors}.",
            projectsScanned, paymentsFetched, invoicesCreated, skipped, errors.Count);

        return Result<SyncEmsPaymentsResult>.Success(result);
    }
}
