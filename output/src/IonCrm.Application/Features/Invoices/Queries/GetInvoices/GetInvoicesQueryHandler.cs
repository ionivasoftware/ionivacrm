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
        IReadOnlyList<Domain.Entities.Invoice> invoices;

        if (request.ProjectId.HasValue)
        {
            // ── Scoped to a single project ────────────────────────────────
            var projectId = request.ProjectId.Value;

            if (!_currentUser.IsSuperAdmin && !_currentUser.ProjectIds.Contains(projectId))
                return Result<List<InvoiceDto>>.Failure("Bu projeye erişim yetkiniz yok.");

            invoices = await _invoiceRepository.GetByProjectIdAsync(projectId, cancellationToken);
        }
        else
        {
            // ── Cross-project: all authorised projects ────────────────────
            // SuperAdmin → null means "no WHERE IN filter" → all invoices
            // Regular user → WHERE ProjectId IN (user's project list)
            List<Guid>? projectIds = _currentUser.IsSuperAdmin
                ? null
                : _currentUser.ProjectIds;

            invoices = await _invoiceRepository.GetAllAsync(projectIds, cancellationToken);
        }

        var dtos = invoices.Select(i => i.ToDto()).ToList();
        return Result<List<InvoiceDto>>.Success(dtos);
    }
}
