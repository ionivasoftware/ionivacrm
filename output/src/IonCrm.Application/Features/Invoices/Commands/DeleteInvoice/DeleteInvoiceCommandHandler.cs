using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Features.Invoices.Commands.DeleteInvoice;

/// <summary>Handles <see cref="DeleteInvoiceCommand"/>.</summary>
public sealed class DeleteInvoiceCommandHandler
    : IRequestHandler<DeleteInvoiceCommand, Result<bool>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DeleteInvoiceCommandHandler> _logger;

    public DeleteInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        ICurrentUserService currentUser,
        ILogger<DeleteInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _currentUser       = currentUser;
        _logger            = logger;
    }

    public async Task<Result<bool>> Handle(
        DeleteInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
        if (invoice is null)
            return Result<bool>.Failure("Fatura bulunamadı.");

        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(invoice.ProjectId))
            return Result<bool>.Failure("Bu faturaya erişim yetkiniz yok.");

        if (invoice.Status != InvoiceStatus.Draft)
            return Result<bool>.Failure("Sadece taslak (Draft) faturalar silinebilir.");

        await _invoiceRepository.DeleteAsync(invoice, cancellationToken);

        _logger.LogInformation(
            "Invoice {InvoiceId} deleted by user in project {ProjectId}.",
            invoice.Id, invoice.ProjectId);

        return Result<bool>.Success(true);
    }
}
