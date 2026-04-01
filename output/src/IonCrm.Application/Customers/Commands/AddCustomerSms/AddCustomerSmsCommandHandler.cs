using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Customers.Commands.AddCustomerSms;

/// <summary>Handles <see cref="AddCustomerSmsCommand"/>.</summary>
public sealed class AddCustomerSmsCommandHandler
    : IRequestHandler<AddCustomerSmsCommand, Result<AddCustomerSmsDto>>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ISaasAClient _saasAClient;
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly IParasutProductRepository _productRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AddCustomerSmsCommandHandler> _logger;

    public AddCustomerSmsCommandHandler(
        ICustomerRepository customerRepository,
        IProjectRepository projectRepository,
        ISaasAClient saasAClient,
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        IParasutProductRepository productRepository,
        ICurrentUserService currentUser,
        ILogger<AddCustomerSmsCommandHandler> logger)
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

        // 3. Get project EMS API key
        var project   = await _projectRepository.GetByIdAsync(customer.ProjectId, cancellationToken);
        var emsApiKey = project?.EmsApiKey;

        // 4. Call EMS API
        EmsAddSmsResponse emsResponse;
        try
        {
            emsResponse = await _saasAClient.AddSmsAsync(emsApiKey, emsCompanyId, request.Count, cancellationToken);
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

        // 5. Try to create Paraşüt draft invoice (best-effort)
        var parasutInvoiceId = await TryCreateParasutDraftInvoiceAsync(
            customer.ProjectId,
            customer.ParasutContactId,
            customer.CompanyName,
            request.Count,
            cancellationToken);

        return Result<AddCustomerSmsDto>.Success(new AddCustomerSmsDto(
            CompanyId:            emsResponse.CompanyId,
            SmsCount:             emsResponse.SmsCount,
            Added:                emsResponse.Added,
            ParasutInvoiceCreated: parasutInvoiceId is not null,
            ParasutInvoiceId:     parasutInvoiceId));
    }

    private async Task<string?> TryCreateParasutDraftInvoiceAsync(
        Guid projectId,
        string? parasutContactId,
        string companyName,
        int count,
        CancellationToken ct)
    {
        try
        {
            var connection = await _connectionRepository.GetByProjectIdAsync(projectId, ct);
            if (connection is null || !connection.IsConnected)
                return null;

            var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
                connection, _parasutClient, _connectionRepository, _logger, ct);
            if (conn is null)
            {
                _logger.LogWarning(
                    "Paraşüt token unavailable for project {ProjectId}: {Error}", projectId, tokenError);
                return null;
            }

            // Look up configured product for this SMS package (e.g. "1000 SMS", "2500 SMS", ...)
            var productName    = $"{count} SMS";
            var configProduct  = await _productRepository.GetByNameAsync(projectId, productName, ct);

            var unitPrice = configProduct?.UnitPrice ?? 0m;
            var vatRate   = configProduct is not null ? (int)(configProduct.TaxRate * 100) : 20;

            var today   = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var dueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

            var lineItem = new SalesInvoiceDetailData
            {
                Attributes = new ParasutSalesInvoiceDetailAttributes(
                    Quantity:       count,
                    UnitPrice:      unitPrice,
                    VatRate:        vatRate,
                    DiscountType:   "percentage",
                    DiscountValue:  0,
                    Description:    $"SMS Kredisi — {companyName}",
                    Unit:           "Adet")
            };

            // If a configured product exists, link it so Paraşüt applies the correct GL account
            if (configProduct is not null && !string.IsNullOrEmpty(configProduct.ParasutProductId))
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
                        Description:           $"{count:N0} SMS Kredisi — {companyName}",
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
                        Contact = string.IsNullOrEmpty(parasutContactId)
                            ? null
                            : new ContactRelationship
                              {
                                  Data = new ContactRelationshipData { Id = parasutContactId }
                              }
                    }
                }
            };

            var result    = await _parasutClient.CreateSalesInvoiceAsync(conn.AccessToken!, conn.CompanyId, invoiceReq, ct);
            var invoiceId = result.Data.Id;
            _logger.LogInformation(
                "Created Paraşüt draft invoice {InvoiceId} for {Count} SMS credits — {CompanyName} (product: {ProductName}).",
                invoiceId, count, companyName, configProduct?.ParasutProductId ?? "(none)");
            return invoiceId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create Paraşüt draft invoice for SMS credits (project {ProjectId}).", projectId);
            return null;
        }
    }
}
