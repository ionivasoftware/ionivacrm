using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.Invoices.Mappings;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Invoices.Commands.UpdateInvoice;

/// <summary>Handles <see cref="UpdateInvoiceCommand"/>.</summary>
public sealed class UpdateInvoiceCommandHandler
    : IRequestHandler<UpdateInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UpdateInvoiceCommandHandler> _logger;

    public UpdateInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        ICurrentUserService currentUser,
        ILogger<UpdateInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(
        UpdateInvoiceCommand request, CancellationToken cancellationToken)
    {
        // 1. Load invoice
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
        if (invoice is null)
            return Result<InvoiceDto>.Failure("Fatura bulunamadı.");

        // 2. Authorize
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(invoice.ProjectId))
            return Result<InvoiceDto>.Failure("Bu faturaya erişim yetkiniz yok.");

        // 3. Only Draft invoices may be edited
        if (invoice.Status != InvoiceStatus.Draft)
            return Result<InvoiceDto>.Failure(
                "Sadece taslak (Draft) faturalar düzenlenebilir. " +
                "Paraşüt'e aktarılmış veya resmileştirilmiş faturalar değiştirilemez.");

        // 4. Parse lines and compute totals (discount-aware)
        var lines = InvoiceLineCalculator.ParseLines(request.LinesJson);
        var (netTotal, grossTotal) = InvoiceLineCalculator.ComputeTotals(lines);

        // 5. Apply changes
        invoice.Title = request.Title;
        invoice.Description = request.Description;
        invoice.InvoiceSeries = request.InvoiceSeries;
        invoice.InvoiceNumber = request.InvoiceNumber;
        invoice.IssueDate = DateTime.SpecifyKind(request.IssueDate, DateTimeKind.Utc);
        invoice.DueDate = DateTime.SpecifyKind(request.DueDate, DateTimeKind.Utc);
        invoice.Currency = request.Currency;
        invoice.LinesJson = request.LinesJson;
        invoice.NetTotal = netTotal;
        invoice.GrossTotal = grossTotal;

        try
        {
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

            _logger.LogInformation(
                "Invoice {InvoiceId} updated in project {ProjectId} " +
                "(NetTotal={NetTotal}, GrossTotal={GrossTotal})",
                invoice.Id, invoice.ProjectId, netTotal, grossTotal);

            return Result<InvoiceDto>.Success(invoice.ToDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update invoice {InvoiceId}: {Error}",
                request.InvoiceId, ex.InnerException?.Message ?? ex.Message);
            return Result<InvoiceDto>.Failure(
                $"Fatura güncellenemedi: {ex.InnerException?.Message ?? ex.Message}");
        }
    }
}
