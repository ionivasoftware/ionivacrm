using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IonCrm.Application.Customers.Commands.AddCustomerSms;

/// <summary>Handles <see cref="AddCustomerSmsCommand"/>.</summary>
public sealed class AddCustomerSmsCommandHandler
    : IRequestHandler<AddCustomerSmsCommand, Result<AddCustomerSmsDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IParasutProductRepository _productRepository;
    private readonly IParasutService _parasutService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AddCustomerSmsCommandHandler> _logger;

    public AddCustomerSmsCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        IInvoiceRepository invoiceRepository,
        IParasutProductRepository productRepository,
        IParasutService parasutService,
        ICurrentUserService currentUser,
        ILogger<AddCustomerSmsCommandHandler> logger)
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

    public async Task<Result<AddCustomerSmsDto>> Handle(
        AddCustomerSmsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Count <= 0)
            return Result<AddCustomerSmsDto>.Failure("SMS adedi 0'dan büyük olmalıdır.");

        // 1. Load customer
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<AddCustomerSmsDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<AddCustomerSmsDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        // 2. Extract EMS company ID from LegacyId
        if (string.IsNullOrEmpty(customer.LegacyId)
            || customer.LegacyId.StartsWith("PC-", StringComparison.OrdinalIgnoreCase))
        {
            return Result<AddCustomerSmsDto>.Failure(
                "Bu müşteri EMS'ten gelmemiş. SMS yükleme yalnızca EMS kaynaklı müşteriler için geçerlidir.");
        }

        string rawId = customer.LegacyId.StartsWith("SAASA-", StringComparison.OrdinalIgnoreCase)
            ? customer.LegacyId["SAASA-".Length..]
            : customer.LegacyId;

        if (!int.TryParse(rawId, out var emsCompanyId))
        {
            return Result<AddCustomerSmsDto>.Failure(
                "Bu müşteri EMS'ten gelmemiş. SMS yükleme yalnızca EMS kaynaklı müşteriler için geçerlidir.");
        }

        // 3. Get project EMS credentials (null → SaasAClient falls back to DI-configured defaults)
        var project    = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var emsApiKey  = project?.EmsApiKey;
        var emsBaseUrl = project?.EmsBaseUrl;

        // 4. Call EMS API
        EmsAddSmsResponse emsResponse;
        try
        {
            emsResponse = await _saasAClient.AddSmsAsync(
                emsApiKey, emsCompanyId, request.Count, cancellationToken, emsBaseUrl);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("BrokenCircuit") ||
                                    ex.Message.Contains("circuit is now open", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("EMS circuit breaker open for customer {CustomerId}.", customer.Id);
            return Result<AddCustomerSmsDto>.Failure(
                "EMS API şu anda geçici olarak erişilemiyor. Lütfen kısa süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EMS add-sms failed for customer {CustomerId} (EMS ID {EmsId}).",
                customer.Id, emsCompanyId);
            return Result<AddCustomerSmsDto>.Failure($"EMS'te SMS yüklenemedi: {ex.Message}");
        }

        _logger.LogInformation(
            "Added {Count} SMS credits for customer {CustomerId} (EMS ID {EmsId}). New total: {Total}.",
            request.Count, customer.Id, emsCompanyId, emsResponse.SmsCount);

        // 5. Create local CRM draft invoice (best-effort)
        var invoiceId = await TryCreateLocalDraftInvoiceAsync(
            customer.ProjectId,
            customer.Id,
            customer.CompanyName,
            request.Count,
            cancellationToken);

        return Result<AddCustomerSmsDto>.Success(new AddCustomerSmsDto(
            CompanyId:     emsResponse.CompanyId,
            SmsCount:      emsResponse.SmsCount,
            Added:         emsResponse.Added,
            InvoiceCreated: invoiceId.HasValue,
            InvoiceId:     invoiceId));
    }

    private async Task<Guid?> TryCreateLocalDraftInvoiceAsync(
        Guid projectId,
        Guid customerId,
        string companyName,
        int count,
        CancellationToken ct)
    {
        try
        {
            var productName   = $"{count} SMS";
            var configProduct = await _productRepository.GetByNameAsync(projectId, productName, ct);

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

                    if (configProduct.TaxRate == 0 && decimal.TryParse(attrs.VatRate,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var vr))
                        configProduct.TaxRate = vr > 1 ? vr / 100m : vr;

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

            decimal unitPrice  = configProduct?.UnitPrice ?? 0m;
            int     vatRate    = configProduct is not null ? (int)(configProduct.TaxRate * 100) : 20;
            decimal netTotal   = unitPrice;
            decimal grossTotal = unitPrice * (1 + vatRate / 100m);

            var lines = new[]
            {
                new
                {
                    description        = !string.IsNullOrEmpty(configProduct?.ParasutProductName)
                                            ? configProduct.ParasutProductName
                                            : $"{count:N0} SMS Kredisi — {companyName}",
                    quantity           = count,
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
                ProjectId   = projectId,
                CustomerId  = customerId,
                Title       = $"{count:N0} SMS Kredisi — {companyName}",
                Description = null,
                IssueDate   = DateTime.UtcNow,
                DueDate     = DateTime.UtcNow.AddDays(30),
                Currency    = "TRL",
                GrossTotal  = grossTotal,
                NetTotal    = netTotal,
                LinesJson   = JsonSerializer.Serialize(lines),
                Status      = InvoiceStatus.Draft
            };

            var created = await _invoiceRepository.AddAsync(invoice, ct);

            _logger.LogInformation(
                "Created local draft invoice {InvoiceId} for {Count} SMS credits — {CompanyName}.",
                created.Id, count, companyName);

            return created.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create local draft invoice for SMS credits (project {ProjectId}).", projectId);
            return null;
        }
    }
}
