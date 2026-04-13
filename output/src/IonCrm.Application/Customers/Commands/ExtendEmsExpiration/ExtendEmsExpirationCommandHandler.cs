using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Application.Customers.Commands.ExtendEmsExpiration;

/// <summary>Handles <see cref="ExtendEmsExpirationCommand"/>.</summary>
public sealed class ExtendEmsExpirationCommandHandler
    : IRequestHandler<ExtendEmsExpirationCommand, Result<ExtendEmsExpirationDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IParasutProductRepository _productRepository;
    private readonly IParasutService _parasutService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ExtendEmsExpirationCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="ExtendEmsExpirationCommandHandler"/>.</summary>
    public ExtendEmsExpirationCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        IInvoiceRepository invoiceRepository,
        IParasutProductRepository productRepository,
        IParasutService parasutService,
        ICurrentUserService currentUser,
        ILogger<ExtendEmsExpirationCommandHandler> logger)
    {
        _customerRepository = customerRepository;
        _projectRepository  = projectRepository;
        _saasAClient        = saasAClient;
        _invoiceRepository  = invoiceRepository;
        _productRepository  = productRepository;
        _parasutService     = parasutService;
        _currentUser        = currentUser;
        _logger             = logger;
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

        // 3. Get project + EMS credentials (null → SaasAClient falls back to DI-configured defaults)
        var project    = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var emsApiKey  = project?.EmsApiKey;
        var emsBaseUrl = project?.EmsBaseUrl;

        // 4. Call EMS API to extend expiration
        EmsExtendExpirationResponse emsResponse;
        try
        {
            emsResponse = await _saasAClient.ExtendExpirationAsync(
                emsApiKey, emsCompanyId, request.DurationType, request.Amount, cancellationToken, emsBaseUrl);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("EMS circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<ExtendEmsExpirationDto>.Failure(
                "EMS API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
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

        // 6. Create local CRM draft invoice for standard billing periods (1 month or 1 year)
        var shouldCreateInvoice =
            (request.DurationType == "Months" && request.Amount == 1) ||
            (request.DurationType == "Years"  && request.Amount == 1);

        if (!shouldCreateInvoice)
            return Result<ExtendEmsExpirationDto>.Success(
                new ExtendEmsExpirationDto(emsResponse.ExpirationDate, false, null));

        var (invoiceId, invoiceError) = await TryCreateLocalDraftInvoiceAsync(
            customer.ProjectId,
            customer.Id,
            customer.CompanyName,
            request.DurationType,
            cancellationToken);

        return Result<ExtendEmsExpirationDto>.Success(
            new ExtendEmsExpirationDto(
                emsResponse.ExpirationDate,
                invoiceId.HasValue,
                invoiceId,
                invoiceError));
    }

    // ── Local CRM draft invoice helper ────────────────────────────────────────

    private async Task<(Guid? InvoiceId, string? Error)> TryCreateLocalDraftInvoiceAsync(
        Guid projectId,
        Guid customerId,
        string companyName,
        string durationType,
        CancellationToken ct)
    {
        try
        {
            var durationLabel = durationType == "Years" ? "1 Yıllık" : "1 Aylık";
            var productName   = durationType == "Years" ? "1 Yıllık Üyelik" : "1 Aylık Üyelik";
            var today         = DateTime.UtcNow;
            var dueDate       = today.AddDays(30);

            // Look up the configured product to get unit price and VAT rate
            var configProduct = await _productRepository.GetByNameAsync(productName, ct);

            if (configProduct is null || string.IsNullOrEmpty(configProduct.ParasutProductId))
            {
                _logger.LogWarning(
                    "Extend EMS draft invoice skipped for customer {CustomerId}: '{Product}' Paraşüt eşleştirmesi yok.",
                    customerId, productName);
                return (null, $"'{productName}' için Paraşüt ürün eşleştirmesi yok. Ayarlar → Paraşüt Ürün Eşleştirmesi'nden tanımlayın.");
            }

            // Auto-enrich from Paraşüt if product data is incomplete
            if (configProduct is not null && !string.IsNullOrEmpty(configProduct.ParasutProductId) &&
                (string.IsNullOrEmpty(configProduct.ParasutProductName) || configProduct.TaxRate == 0 || configProduct.UnitPrice == 0))
            {
                var (parasutData, _) = await _parasutService.GetProductByIdAsync(
                    projectId, configProduct.ParasutProductId, ct);

                if (parasutData?.Data?.Attributes is { } attrs)
                {
                    if (string.IsNullOrEmpty(configProduct.ParasutProductName))
                        configProduct.ParasutProductName = attrs.Name;

                    if (configProduct.TaxRate == 0 && attrs.VatRateInt is { } vr)
                        configProduct.TaxRate = vr / 100m;

                    if (configProduct.UnitPrice == 0)
                    {
                        var priceStr = attrs.SalesPrice ?? attrs.ListPrice ?? attrs.SalesPriceInTrl;
                        if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var p))
                            configProduct.UnitPrice = p;
                    }

                    await _productRepository.UpdateAsync(configProduct, ct);
                }
            }

            decimal unitPrice = configProduct?.UnitPrice ?? 0m;
            int     vatRate   = configProduct is not null ? (int)(configProduct.TaxRate * 100) : 20;
            decimal netTotal  = unitPrice;
            decimal grossTotal = unitPrice * (1 + vatRate / 100m);

            var lines = new[]
            {
                new
                {
                    description        = !string.IsNullOrEmpty(configProduct?.ParasutProductName)
                                            ? configProduct.ParasutProductName
                                            : $"{durationLabel} EMS Lisans — {companyName}",
                    quantity           = 1,
                    unitPrice,
                    vatRate,
                    discountValue      = 0,
                    discountType       = "percentage",
                    unit               = "Adet",
                    parasutProductId   = configProduct?.ParasutProductId,
                    parasutProductName = configProduct?.ParasutProductName
                }
            };

            var invoice = new Invoice
            {
                ProjectId    = projectId,
                CustomerId   = customerId,
                Title        = $"{durationLabel} EMS Lisans Yenileme — {companyName}",
                Description  = null,
                IssueDate    = today,
                DueDate      = dueDate,
                Currency     = "TRL",
                GrossTotal   = grossTotal,
                NetTotal     = netTotal,
                LinesJson    = JsonSerializer.Serialize(lines),
                Status       = InvoiceStatus.Draft
            };

            var created = await _invoiceRepository.AddAsync(invoice, ct);

            _logger.LogInformation(
                "Created local draft invoice {InvoiceId} for customer {CompanyName} ({Duration}).",
                created.Id, companyName, durationLabel);

            return (created.Id, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create local draft invoice for project {ProjectId}. Error: {Error}",
                projectId, ex.Message);
            return (null, $"Taslak fatura oluşturulamadı: {ex.Message}");
        }
    }
}
