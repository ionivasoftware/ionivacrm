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
    List<LiftdeskChecklistHeaderInput> Headers) : IRequest<Result<LiftdeskChecklistDoc>>;
