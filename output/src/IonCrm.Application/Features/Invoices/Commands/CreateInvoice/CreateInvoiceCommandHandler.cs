using System.Text.Json;
using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Invoices.Mappings;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Invoices.Commands.CreateInvoice;

/// <summary>Handles <see cref="CreateInvoiceCommand"/>.</summary>
public sealed class CreateInvoiceCommandHandler
    : IRequestHandler<CreateInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateInvoiceCommandHandler> _logger;

    public CreateInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        ICustomerRepository customerRepository,
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ICurrentUserService currentUser,
        ILogger<CreateInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _customerRepository = customerRepository;
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(
        CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        // Validate customer exists and is accessible
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return Result<InvoiceDto>.Failure("Müşteri bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(customer.ProjectId))
            return Result<InvoiceDto>.Failure("Bu müşteriye erişim yetkiniz yok.");

        var invoice = new Invoice
        {
            ProjectId = customer.ProjectId,
            CustomerId = request.CustomerId,
            Title = request.Title,
            Description = request.Description,
            InvoiceSeries = request.InvoiceSeries,
            InvoiceNumber = request.InvoiceNumber,
            IssueDate = DateTime.SpecifyKind(request.IssueDate, DateTimeKind.Utc),
            DueDate = DateTime.SpecifyKind(request.DueDate, DateTimeKind.Utc),
            Currency = request.Currency,
            GrossTotal = request.GrossTotal,
            NetTotal = request.NetTotal,
            LinesJson = request.LinesJson,
            Status = InvoiceStatus.Draft
        };

        try
        {
            await _invoiceRepository.AddAsync(invoice, cancellationToken);

            _logger.LogInformation(
                "Invoice {InvoiceId} created for customer {CustomerId} in project {ProjectId}",
                invoice.Id, invoice.CustomerId, invoice.ProjectId);

            // ── Auto-transfer + officialize if customer has e-invoice info ────────
            if (customer.IsEInvoicePayer
                && !string.IsNullOrEmpty(customer.ParasutContactId)
                && !string.IsNullOrEmpty(customer.EInvoiceAddress))
            {
                await TryAutoTransferAndOfficializeAsync(invoice, customer, cancellationToken);
            }

            return Result<InvoiceDto>.Success(invoice.ToDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create invoice for customer {CustomerId}: {Error}",
                request.CustomerId, ex.InnerException?.Message ?? ex.Message);
            return Result<InvoiceDto>.Failure($"Fatura oluşturulamadı: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    /// <summary>
    /// When a customer is a known e-invoice payer with a linked Paraşüt contact,
    /// automatically transfers the invoice to Paraşüt and officializes it.
    /// This is best-effort: failures are logged but the invoice creation still succeeds (stays Draft).
    /// </summary>
    private async Task TryAutoTransferAndOfficializeAsync(
        Invoice invoice, Customer customer, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Get Paraşüt connection + ensure valid token
            var connection = await _connectionRepository.GetByProjectIdAsync(
                invoice.ProjectId, cancellationToken);

            var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
                connection, _parasutClient, _connectionRepository, _logger, cancellationToken);

            if (conn is null)
            {
                _logger.LogWarning(
                    "Auto-transfer skipped for invoice {InvoiceId}: {Error}",
                    invoice.Id, tokenError);
                return;
            }

            // 2. Build Paraşüt sales invoice request
            var lines = ParseLines(invoice.LinesJson);
            var details = lines.Select(l => new SalesInvoiceDetailData
            {
                Attributes = new ParasutSalesInvoiceDetailAttributes(
                    Quantity:      l.Quantity,
                    UnitPrice:     l.UnitPrice,
                    VatRate:       l.VatRate,
                    DiscountType:  l.DiscountType ?? "percentage",
                    DiscountValue: l.DiscountValue,
                    Description:   l.Description,
                    Unit:          l.Unit ?? "Adet")
            }).ToList();

            var relationships = new CreateSalesInvoiceRelationships
            {
                Details = new SalesInvoiceDetailsRelationship { Data = details },
                Contact = new ContactRelationship
                {
                    Data = new ContactRelationshipData { Id = customer.ParasutContactId! }
                }
            };

            var invoiceRequest = new CreateSalesInvoiceRequest
            {
                Data = new CreateSalesInvoiceData
                {
                    Attributes = new ParasutSalesInvoiceAttributes(
                        ItemType:           "invoice",
                        Description:        invoice.Description,
                        IssueDate:          invoice.IssueDate.ToString("yyyy-MM-dd"),
                        DueDate:            invoice.DueDate.ToString("yyyy-MM-dd"),
                        InvoiceSeries:      invoice.InvoiceSeries,
                        InvoiceId:          invoice.InvoiceNumber,
                        Currency:           invoice.Currency,
                        ExchangeRate:       null,
                        WithholdingRate:    null,
                        VatWithholdingRate: null,
                        InvoiceDiscountType: null,
                        InvoiceDiscount:    null,
                        BillingAddress:     null,
                        BillingPhone:       null,
                        BillingFax:         null,
                        TaxOffice:          customer.TaxUnit,
                        TaxNumber:          customer.TaxNumber,
                        City:               null,
                        District:           null,
                        PaymentAccountId:   null),
                    Relationships = relationships
                }
            };

            // 3. Create sales invoice on Paraşüt
            var parasutResult = await _parasutClient.CreateSalesInvoiceAsync(
                conn.AccessToken!,
                conn.CompanyId,
                invoiceRequest,
                cancellationToken);

            var parasutInvoiceId = parasutResult.Data.Id!;
            invoice.ParasutId = parasutInvoiceId;
            invoice.Status = InvoiceStatus.TransferredToParasut;

            _logger.LogInformation(
                "Auto-transferred invoice {InvoiceId} to Paraşüt as {ParasutId}",
                invoice.Id, parasutInvoiceId);

            // 4. Officialize: e-invoice for e-invoice payers, e-archive otherwise
            try
            {
                if (customer.IsEInvoicePayer)
                {
                    var eInvoiceResult = await _parasutClient.CreateEInvoiceAsync(
                        conn.AccessToken!, conn.CompanyId, parasutInvoiceId, cancellationToken);

                    _logger.LogInformation(
                        "Auto-officialized invoice {InvoiceId} as e-Invoice (UUID={Uuid})",
                        invoice.Id, eInvoiceResult.Data?.Attributes?.Uuid);
                }
                else
                {
                    var eArchiveResult = await _parasutClient.CreateEArchiveAsync(
                        conn.AccessToken!, conn.CompanyId, parasutInvoiceId, cancellationToken);

                    _logger.LogInformation(
                        "Auto-officialized invoice {InvoiceId} as e-Archive (UUID={Uuid})",
                        invoice.Id, eArchiveResult.Data?.Attributes?.Uuid);
                }

                invoice.Status = InvoiceStatus.Officialized;
            }
            catch (Exception ex)
            {
                // Officialize failed but transfer succeeded — invoice stays as TransferredToParasut
                _logger.LogWarning(ex,
                    "Auto-officialize failed for invoice {InvoiceId} (ParasutId={ParasutId}): {Error}. Invoice was transferred but not officialized.",
                    invoice.Id, parasutInvoiceId, ex.InnerException?.Message ?? ex.Message);
            }

            // 5. Persist updated status + ParasutId
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }
        catch (Exception ex)
        {
            // Auto-transfer failed entirely — invoice stays as Draft, creation still succeeds
            _logger.LogWarning(ex,
                "Auto-transfer to Paraşüt failed for invoice {InvoiceId}: {Error}. Invoice saved as Draft.",
                invoice.Id, ex.InnerException?.Message ?? ex.Message);
        }
    }

    /// <summary>Parses the denormalized LinesJson into structured line items.</summary>
    private static List<InvoiceLine> ParseLines(string linesJson)
    {
        if (string.IsNullOrWhiteSpace(linesJson) || linesJson == "[]")
            return new List<InvoiceLine>();

        try
        {
            return JsonSerializer.Deserialize<List<InvoiceLine>>(linesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<InvoiceLine>();
        }
        catch
        {
            return new List<InvoiceLine>();
        }
    }

    /// <summary>Internal model for parsing LinesJson.</summary>
    private sealed record InvoiceLine
    {
        public string? Description { get; init; }
        public decimal Quantity { get; init; } = 1;
        public decimal UnitPrice { get; init; }
        public int VatRate { get; init; }
        public decimal DiscountValue { get; init; }
        public string? DiscountType { get; init; }
        public string? Unit { get; init; }
    }
}
