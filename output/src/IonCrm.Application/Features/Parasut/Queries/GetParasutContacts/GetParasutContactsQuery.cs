using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutContacts;

/// <summary>Returns a paginated list of contacts from Paraşüt.</summary>
public record GetParasutContactsQuery(
    Guid ProjectId,
    int  Page     = 1,
    int  PageSize = 25
) : IRequest<Result<GetParasutContactsDto>>;

/// <summary>Paginated contact list from Paraşüt.</summary>
public record GetParasutContactsDto(
    List<ParasutContactItem> Items,
    int TotalCount,
    int TotalPages,
    int CurrentPage
);

/// <summary>Single contact item in the list.</summary>
public record ParasutContactItem(
    string  Id,
    string  Name,
    string? Email,
    string? Phone,
    string  ContactType,
    string  AccountType,
    string? TaxNumber
);
