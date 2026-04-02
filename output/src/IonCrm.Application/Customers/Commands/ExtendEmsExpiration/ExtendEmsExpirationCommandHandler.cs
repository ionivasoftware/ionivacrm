using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.ExtendEmsExpiration;

/// <summary>Handles <see cref="ExtendEmsExpirationCommand"/>.</summary>
public sealed class ExtendEmsExpirationCommandHandler
    : IRequestHandler<ExtendEmsExpirationCommand, Result<ExtendEmsExpirationDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly IParasutProductRepository _productRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ExtendEmsExpirationCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="ExtendEmsExpirationCommandHandler"/>.</summary>
    public ExtendEmsExpirationCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        IParasutProductRepository productRepository,
        ICurrentUserService currentUser,
        ILogger<ExtendEmsExpirationCommandHandler> logger)
    {
        _customerRepository   = customerRepository;
        _projectRepository    = projectRepository;
        _saasAClient          = saasAClient;
        _parasutClient        = parasutClient;
        _connectionRepository = connectionRepository;
        _productRepository    = productRepository;
        _currentUser          = currentUser;
        _logger               = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ExtendEmsExpirationDto>> Handle(
        ExtendEmsExpirationCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<ExtendEmsExpirationDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<ExtendEmsExpirationDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 2. Verify this is an EMS customer and extract the numeric EMS company ID.
        //    LegacyId formats:
        //      "3"        → plain numeric (original DB migration + new EMS CRM sync canonical format)
        //      "SAASA-3"  → prefixed (created by earlier sync runs before normalization)
        //      "PC-123"   → PotentialCustomer (not EMS — skip)
        if (string.IsNullOrEmpty(customer.LegacyId)
            || customer.LegacyId.StartsWith("PC-", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ExtendEmsExpirationDto>.Failure(
                "Bu müşteri EMS'ten gelmemiş. Süre uzatma yalnızca EMS kaynaklı müşteriler için geçerlidir.");
        }

        string rawId = customer.LegacyId.StartsWith("SAASA-", StringComparison.OrdinalIgnoreCase)
            ? customer.LegacyId["SAASA-".Length..]
            : customer.LegacyId;

        if (!int.TryParse(rawId, out var emsCompanyId))
        {
            return Result<ExtendEmsExpirationDto>.Failure(
                "Bu müşteri EMS'ten gelmemiş. Süre uzatma yalnızca EMS kaynaklı müşteriler için geçerlidir.");
        }

        // 3. Get project + EMS API key (null → SaasAClient falls back to DI-configured key)
        var project = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var emsApiKey = project?.EmsApiKey;

        // 4. Call EMS API to extend expiration
        EmsExtendExpirationResponse emsResponse;
        try
        {
            emsResponse = await _saasAClient.ExtendExpirationAsync(
                emsApiKey, emsCompanyId, request.DurationType, request.Amount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EMS extend-expiration failed for customer {CustomerId} (EMS ID {EmsId}).",
                customer.Id, emsCompanyId);
            return Result<ExtendEmsExpirationDto>.Failure(
                $"EMS'te süre uzatılamadı: {ex.Message}");
        }

        // 5. Update local ExpirationDate
        customer.ExpirationDate = emsResponse.ExpirationDate;
        await _customerRepository.UpdateAsync(customer, cancellationToken);

        _logger.LogInformation(
            "Extended EMS expiration for customer {CustomerId}. New expiry: {Expiry:d}. Duration: {Amt} {Type}.",
            customer.Id, emsResponse.ExpirationDate, request.Amount, request.DurationType);

        // 6. Create Paraşüt draft invoice for standard billing periods (1 month or 1 year)
        var shouldCreateInvoice =
            (request.DurationType == "Months" && request.Amount == 1) ||
            (request.DurationType == "Years"  && request.Amount == 1);

        if (!shouldCreateInvoice)
            return Result<ExtendEmsExpirationDto>.Success(
                new ExtendEmsExpirationDto(emsResponse.ExpirationDate, false, null));

        var (parasutInvoiceId, parasutError) = await TryCreateParasutDraftInvoiceAsync(
            customer.ProjectId,
            customer.ParasutContactId,
            customer.CompanyName,
            request.DurationType,
            request.Amount,
            cancellationToken);

        return Result<ExtendEmsExpirationDto>.Success(
            new ExtendEmsExpirationDto(
                emsResponse.ExpirationDate,
                parasutInvoiceId is not null,
                parasutInvoiceId,
                parasutError));
    }

    // ── Paraşüt draft invoice helper ──────────────────────────────────────────

    private async Task<(string? InvoiceId, string? Error)> TryCreateParasutDraftInvoiceAsync(
        Guid projectId,
        string? parasutContactId,
        string companyName,
        string durationType,
        int amount,
        CancellationToken ct)
    {
        // Pre-check: Paraşüt requires a linked contact to create a sales invoice
        if (string.IsNullOrEmpty(parasutContactId))
        {
            _logger.LogDebug(
                "Skipping Paraşüt draft invoice — customer has no linked Paraşüt contact (project {ProjectId}).", projectId);
            return (null, "Müşteri henüz Paraşüt'e eşleştirilmemiş. Müşteri detayından 'Paraşüt'e Aktar' yapın.");
        }

        try
        {
            var connection = await _connectionRepository.GetByProjectIdAsync(projectId, ct);
            if (connection is null || !connection.IsConnected)
            {
                _logger.LogDebug(
                    "Skipping Paraşüt draft invoice — no active connection for project {ProjectId}.", projectId);
                return (null, "Paraşüt bağlantısı aktif değil.");
            }

            var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
                connection, _parasutClient, _connectionRepository, _logger, ct);
            if (conn is null)
            {
                _logger.LogWarning(
                    "Paraşüt token unavailable for project {ProjectId}: {Error}", projectId, tokenError);
                return (null, $"Paraşüt token alınamadı: {tokenError}");
            }

            var durationLabel = durationType == "Years" ? "1 Yıllık" : "1 Aylık";
            var today    = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var dueDate  = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

            // Look up the configured Paraşüt product for this membership type
            var productName   = durationType == "Years" ? "1 Yıllık Üyelik" : "1 Aylık Üyelik";
            var configProduct = await _productRepository.GetByNameAsync(projectId, productName, ct);

            if (configProduct is null)
            {
                _logger.LogDebug(
                    "Skipping Paraşüt draft invoice — product '{ProductName}' not configured for project {ProjectId}.",
                    productName, projectId);
                return (null, $"Paraşüt ürün eşleşmesi eksik: '{productName}' için Ayarlar > Paraşüt'ten ürün eşleştirin.");
            }

            var unitPrice = configProduct.UnitPrice;
            var vatRate   = (int)(configProduct.TaxRate * 100);

            var lineItem = new SalesInvoiceDetailData
            {
                Attributes = new ParasutSalesInvoiceDetailAttributes(
                    Quantity:       1,
                    UnitPrice:      unitPrice,
                    VatRate:        vatRate,
                    DiscountType:   "percentage",
                    DiscountValue:  0,
                    Description:    $"{durationLabel} EMS Lisans — {companyName}",
                    Unit:           "Adet")
            };

            // Link the configured product so Paraşüt applies the correct GL account
            if (!string.IsNullOrEmpty(configProduct.ParasutProductId))
            {
                lineItem.Relationships = new SalesInvoiceDetailRelationships
                {
                    Product = new ProductRelationship
                    {
                        Data = new ProductRelationshipData { Id = configProduct.ParasutProductId }
                    }
                };
            }

            var invoiceReq = new CreateSalesInvoiceRequest
            {
                Data = new CreateSalesInvoiceData
                {
                    Attributes = new ParasutSalesInvoiceAttributes(
                        ItemType:              "invoice",
                        Description:           $"{durationLabel} EMS Lisans Yenileme — {companyName}",
                        IssueDate:             today,
                        DueDate:               dueDate,
                        InvoiceSeries:         null,
                        InvoiceId:             null,
                        Currency:              "TRL",
                        ExchangeRate:          null,
                        WithholdingRate:       null,
                        VatWithholdingRate:    null,
                        InvoiceDiscountType:   null,
                        InvoiceDiscount:       null,
                        BillingAddress:        null,
                        BillingPhone:          null,
                        BillingFax:            null,
                        TaxOffice:             null,
                        TaxNumber:             null,
                        City:                  null,
                        District:              null,
                        PaymentAccountId:      null),
                    Relationships = new CreateSalesInvoiceRelationships
                    {
                        Details = new SalesInvoiceDetailsRelationship
                        {
                            Data = new List<SalesInvoiceDetailData> { lineItem }
                        },
                        Contact = new ContactRelationship
                        {
                            Data = new ContactRelationshipData { Id = parasutContactId }
                        }
                    }
                }
            };

            var result = await _parasutClient.CreateSalesInvoiceAsync(
                conn.AccessToken!, conn.CompanyId, invoiceReq, ct);

            var invoiceId = result.Data.Id;
            _logger.LogInformation(
                "Created Paraşüt draft invoice {InvoiceId} for customer {CompanyName} ({Duration}, product: {ProductId}).",
                invoiceId, companyName, durationLabel, configProduct.ParasutProductId ?? "(none)");

            return (invoiceId, null);
        }
        catch (Exception ex)
        {
            // Non-fatal — the expiration was already extended; log and continue
            _logger.LogWarning(ex,
                "Failed to create Paraşüt draft invoice for project {ProjectId}. Error: {Error}",
                projectId, ex.Message);
            return (null, $"Paraşüt fatura oluşturulamadı: {ex.Message}");
        }
    }
}
