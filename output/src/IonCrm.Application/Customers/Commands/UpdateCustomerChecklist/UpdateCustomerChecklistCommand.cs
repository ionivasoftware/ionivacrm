using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using MediatR;

namespace IonCrm.Application.Customers.Commands.UpdateCustomerChecklist;

/// <summary>
/// Replaces the maintenance or fault checklist of a Liftdesk-sourced customer with the given set
/// (full-document replace — the Liftdesk side deletes the old rows and writes these). The array
/// order becomes the SortOrder; an empty list intentionally clears the checklist.
/// <paramref name="Kind"/> is "maintenance" or "fault".
/// </summary>
public sealed record UpdateCustomerChecklistCommand(
    Guid CustomerId,
    string Kind,
    List<LiftdeskChecklistHeaderInput> Headers,
    /// <summary>
    /// Maintenance equipment family the replace is scoped to (1 = elevator, 2 = escalator). MUST match
    /// the type the list was read with, otherwise the wrong family would be overwritten.
    /// </summary>
    int? Type = null,
    /// <summary>
    /// Language the replace is scoped to. MUST match the culture the list was read with — the other
    /// languages are left intact, so a mismatch would overwrite the wrong one.
    /// </summary>
    int? Culture = null) : IRequest<Result<LiftdeskChecklistDoc>>;
