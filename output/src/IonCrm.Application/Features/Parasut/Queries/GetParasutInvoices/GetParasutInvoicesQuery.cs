using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutInvoices;

/// <summary>Returns a paginated list of sales invoices from Paraşüt.</summary>
public record GetParasutInvoicesQuery(
    Guid ProjectId,
    int  Page     = 1,
    int  PageSize = 25
) : IRequest<Result<GetParasutInvoicesDto>>;

/// <summary>Paginated invoice list from Paraşüt.</summary>
public record GetParasutInvoicesDto(
    List<ParasutInvoiceItem> Items,
    int TotalCount,
    int TotalPages,
    int CurrentPage
);

/// <summary>Single invoice item in the list.</summary>
public record ParasutInvoiceItem(
    string  Id,
    string  IssueDate,
    string  DueDate,
    string  Currency,
    decimal GrossTotal,
    decimal NetTotal,
    decimal TotalPaid,
    decimal Remaining,
    string? Description,
    string? ArchivingStatus
);
