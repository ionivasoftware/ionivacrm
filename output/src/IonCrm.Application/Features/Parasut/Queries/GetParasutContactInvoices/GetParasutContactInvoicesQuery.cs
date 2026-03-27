using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.Parasut.Queries.GetParasutInvoices;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutContactInvoices;

/// <summary>Returns a paginated list of sales invoices for a specific Paraşüt contact (cari hareketleri).</summary>
public record GetParasutContactInvoicesQuery(
    Guid   ProjectId,
    string ParasutContactId,
    int    Page     = 1,
    int    PageSize = 25
) : IRequest<Result<GetParasutInvoicesDto>>;
