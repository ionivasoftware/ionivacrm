using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Parasut.Commands.CreateSalesInvoice;

/// <summary>Handles <see cref="CreateParasutSalesInvoiceCommand"/>.</summary>
public sealed class CreateParasutSalesInvoiceCommandHandler
    : IRequestHandler<CreateParasutSalesInvoiceCommand, Result<CreateParasutSalesInvoiceDto>>
{
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ILogger<CreateParasutSalesInvoiceCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="CreateParasutSalesInvoiceCommandHandler"/>.</summary>
    public CreateParasutSalesInvoiceCommandHandler(
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ILogger<CreateParasutSalesInvoiceCommandHandler> logger)
    {
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<CreateParasutSalesInvoiceDto>> Handle(
        CreateParasutSalesInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load connection
        var connection = await _connectionRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        if (connection is null || !connection.IsConnected)
            return Result<CreateParasutSalesInvoiceDto>.Failure(
                "Paraşüt bağlantısı bulunamadı veya token süresi dolmuş.");

        try
        {
            // 2. Build line items
            var details = request.Lines.Select(l => new SalesInvoiceDetailData
            {
                Attributes = new ParasutSalesInvoiceDetailAttributes(
                    Quantity:       l.Quantity,
                    UnitPrice:      l.UnitPrice,
                    VatRate:        l.VatRate,
                    DiscountType:   l.DiscountType,
                    DiscountValue:  l.DiscountValue,
                    Description:    l.Description,
                    Unit:           l.Unit)
            }).ToList();

            // 3. Build relationships
            var relationships = new CreateSalesInvoiceRelationships
            {
                Details = new SalesInvoiceDetailsRelationship { Data = details }
            };

            if (!string.IsNullOrEmpty(request.ParasutContactId))
            {
                relationships.Contact = new ContactRelationship
                {
                    Data = new ContactRelationshipData { Id = request.ParasutContactId }
                };
            }

            // 4. Build request
            var invoiceRequest = new CreateSalesInvoiceRequest
            {
                Data = new CreateSalesInvoiceData
                {
                    Attributes = new ParasutSalesInvoiceAttributes(
                        ItemType:      "invoice",
                        Description:   request.Description,
                        IssueDate:     request.IssueDate,
                        DueDate:       request.DueDate,
                        InvoiceSeries: request.InvoiceSeries,
                        InvoiceId:     request.InvoiceId,
                        Currency:      request.Currency,
                        ExchangeRate:  null,
                        WithholdingRate: null,
                        VatWithholdingRate: null,
                        InvoiceDiscountType: null,
                        InvoiceDiscount: null,
                        BillingAddress: null,
                        BillingPhone:  null,
                        BillingFax:    null,
                        TaxOffice:     null,
                        TaxNumber:     null,
                        City:          null,
                        District:      null,
                        PaymentAccountId: null),
                    Relationships = relationships
                }
            };

            // 5. Send to Paraşüt
            var result = await _parasutClient.CreateSalesInvoiceAsync(
                connection.AccessToken!,
                connection.CompanyId,
                invoiceRequest,
                cancellationToken);

            var attr = result.Data.Attributes;

            _logger.LogInformation(
                "Created Paraşüt sales invoice {InvoiceId} for project {ProjectId}.",
                result.Data.Id, request.ProjectId);

            return Result<CreateParasutSalesInvoiceDto>.Success(new CreateParasutSalesInvoiceDto(
                ParasutInvoiceId: result.Data.Id ?? string.Empty,
                IssueDate:        attr.IssueDate,
                DueDate:          attr.DueDate,
                GrossTotal:       attr.GrossTotal ?? 0,
                NetTotal:         attr.NetTotal ?? 0,
                Currency:         attr.Currency));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create Paraşüt invoice for project {ProjectId}.", request.ProjectId);
            return Result<CreateParasutSalesInvoiceDto>.Failure(
                $"Fatura oluşturulamadı: {ex.Message}");
        }
    }
}
