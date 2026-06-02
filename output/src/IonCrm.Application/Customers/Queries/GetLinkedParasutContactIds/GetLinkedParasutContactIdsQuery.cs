using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetLinkedParasutContactIds;

/// <summary>
/// Returns the list of Paraşüt contact IDs already linked to a customer in the given project.
/// Used by the link-contact UI to filter out already-taken contacts from the picker, so the
/// same Paraşüt cari can't be double-linked.
/// </summary>
public record GetLinkedParasutContactIdsQuery(Guid ProjectId)
    : IRequest<Result<IReadOnlyList<string>>>;
