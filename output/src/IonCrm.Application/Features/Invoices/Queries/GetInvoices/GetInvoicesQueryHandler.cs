using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.Invoices.Mappings;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Features.Invoices.Queries.GetInvoices;

/// <summary>Handles <see cref="GetInvoicesQuery"/>.</summary>
public sealed class GetInvoicesQueryHandler
    : IRequestHandler<GetInvoicesQuery, Result<List<InvoiceDto>>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentUserService _currentUser;

    public GetInvoicesQueryHandler(
        IInvoiceRepository invoiceRepository,
        ICurrentUserService currentUser)
    {
        _invoiceRepository = invoiceRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<List<InvoiceDto>>> Handle(
        GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(request.ProjectId))
            return Result<List<InvoiceDto>>.Failure("Bu projeye erişim yetkiniz yok.");

        var invoices = await _invoiceRepository.GetByProjectIdAsync(
            request.ProjectId, cancellationToken);

        var dtos = invoices.Select(i => i.ToDto()).ToList();
        return Result<List<InvoiceDto>>.Success(dtos);
    }
}
