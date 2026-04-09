using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Invoices;
using IonCrm.Application.Features.Invoices.Mappings;
using IonCrm.Application.Features.Parasut;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Invoices.Commands.TransferInvoiceToParasut;

/// <summary>Handles <see cref="TransferInvoiceToParasutCommand"/>.</summary>
public sealed class TransferInvoiceToParasutCommandHandler
    : IRequestHandler<TransferInvoiceToParasutCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IParasutClient _parasutClient;
    private readonly IParasutConnectionRepository _connectionRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<TransferInvoiceToParasutCommandHandler> _logger;

    public TransferInvoiceToParasutCommandHandler(
        IInvoiceRepository invoiceRepository,
        ICustomerRepository customerRepository,
        IParasutClient parasutClient,
        IParasutConnectionRepository connectionRepository,
        ICurrentUserService currentUser,
        ILogger<TransferInvoiceToParasutCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _customerRepository = customerRepository;
        _parasutClient = parasutClient;
        _connectionRepository = connectionRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(
        TransferInvoiceToParasutCommand request, CancellationToken cancellationToken)
    {
        // 1. Load invoice
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
        if (invoice is null)
            return Result<InvoiceDto>.Failure("Fatura bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(invoice.ProjectId))
            return Result<InvoiceDto>.Failure("Bu faturaya erişim yetkiniz yok.");

        if (invoice.Status != InvoiceStatus.Draft)
            return Result<InvoiceDto>.Failure("Sadece taslak faturalar Paraşüt'e aktarılabilir.");

        if (!string.IsNullOrEmpty(invoice.ParasutId))
            return Result<InvoiceDto>.Failure("Bu fatura zaten Paraşüt'e aktarılmış.");

        // 2. Load Paraşüt connection (project-specific first, fall back to global) + refresh token
        var connection = await _connectionRepository.GetEffectiveConnectionAsync(
            invoice.ProjectId, cancellationToken);

        var (conn, tokenError) = await ParasutTokenHelper.EnsureValidTokenAsync(
            connection, _parasutClient, _connectionRepository, _logger, cancellationToken);
        if (conn is null)
            return Result<InvoiceDto>.Failure(tokenError ?? "Paraşüt bağlantısı bulunamadı.");

        try
        {
            // 3. Parse LinesJson to build Paraşüt line items
            var lines = InvoiceLineCalculator.ParseLines(invoice.LinesJson);

            var details = lines.Select(l => new SalesInvoiceDetailData
            {
                Attributes = new ParasutSalesInvoiceDetailAttributes(
                    Quantity:      l.Quantity,
                    UnitPrice:     l.UnitPrice,
                    VatRate:       l.VatRate,
                    DiscountType:  l.DiscountType == "amount" ? "amount" : "percentage",
                    DiscountValue: l.DiscountValue,
                    Description:   l.Description,
                    Unit:          l.Unit ?? "Adet"),
                Relationships = !string.IsNullOrEmpty(l.ParasutProductId)
                    ? new SalesInvoiceDetailRelationships
                    {
                        Product = new ProductRelationship
                        {
                            Data = new ProductRelationshipData { Id = l.ParasutProductId }
                        }
                    }
                    : null
            }).ToList();

            // 4. Build relationships
            var relationships = new CreateSalesInvoiceRelationships
            {
                Details = new SalesInvoiceDetailsRelationship { Data = details }
            };

            // Link to Paraşüt contact if customer has ParasutContactId
            var customer = await _customerRepository.GetByIdAsync(invoice.CustomerId, cancellationToken);
            if (customer is not null && !string.IsNullOrEmpty(customer.ParasutContactId))
            {
                relationships.Contact = new ContactRelationship
                {
                    Data = new ContactRelationshipData { Id = customer.ParasutContactId }
                };
            }

            // 5. Build Paraşüt request
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
                        TaxOffice:          null,
                        TaxNumber:          null,
                        City:               null,
                        District:           null,
                        PaymentAccountId:   null),
                    Relationships = relationships
                }
            };

            // 6. Send to Paraşüt
            var result = await _parasutClient.CreateSalesInvoiceAsync(
                conn.AccessToken!,
                conn.CompanyId,
                invoiceRequest,
                cancellationToken);

            // 7. Update CRM invoice with Paraşüt ID
            invoice.ParasutId = result.Data.Id;
            invoice.Status = InvoiceStatus.TransferredToParasut;
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

            _logger.LogInformation(
                "Invoice {InvoiceId} transferred to Paraşüt as {ParasutId} for project {ProjectId}",
                invoice.Id, invoice.ParasutId, invoice.ProjectId);

            // 8. Fetch e-invoice info from Paraşüt and update customer
            await TryUpdateEInvoiceInfoAsync(customer, conn.AccessToken!, conn.CompanyId, cancellationToken);

            return Result<InvoiceDto>.Success(invoice.ToDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to transfer invoice {InvoiceId} to Paraşüt: {Error}",
                request.InvoiceId, ex.InnerException?.Message ?? ex.Message);
            return Result<InvoiceDto>.Failure(
                $"Fatura Paraşüt'e aktarılamadı: {ex.Message}");
        }
    }

    /// <summary>
    /// Queries the Paraşüt e-invoice inbox API using the customer's tax number.
    /// If the customer is an e-invoice payer, updates IsEInvoicePayer and EInvoiceAddress.
    /// This is best-effort — failures are logged but do not fail the transfer.
    /// </summary>
    private async Task TryUpdateEInvoiceInfoAsync(
        Domain.Entities.Customer? customer,
        string accessToken,
        long companyId,
        CancellationToken cancellationToken)
    {
        if (customer is null || string.IsNullOrWhiteSpace(customer.TaxNumber))
            return;

        try
        {
            var inboxes = await _parasutClient.GetEInvoiceInboxesAsync(
                accessToken, companyId, customer.TaxNumber, cancellationToken);

            var isEInvoicePayer = inboxes.Data?.Count > 0;
            var eInvoiceAddress = inboxes.Data?.FirstOrDefault()?.Attributes?.EInvoiceAddress;

            // Only update if there is a change
            if (customer.IsEInvoicePayer != isEInvoicePayer || customer.EInvoiceAddress != eInvoiceAddress)
            {
                customer.IsEInvoicePayer = isEInvoicePayer;
                customer.EInvoiceAddress = eInvoiceAddress;
                await _customerRepository.UpdateAsync(customer, cancellationToken);

                _logger.LogInformation(
                    "Updated e-invoice info for customer {CustomerId}: IsEInvoicePayer={IsEInvoicePayer}, Address={Address}",
                    customer.Id, isEInvoicePayer, eInvoiceAddress ?? "(none)");
            }
        }
        catch (Exception ex)
        {
            // Best-effort: don't fail the invoice transfer because of e-invoice lookup
            _logger.LogWarning(ex,
                "Failed to fetch e-invoice info for customer {CustomerId} (VKN={Vkn}): {Error}",
                customer.Id, customer.TaxNumber, ex.InnerException?.Message ?? ex.Message);
        }
    }
}
