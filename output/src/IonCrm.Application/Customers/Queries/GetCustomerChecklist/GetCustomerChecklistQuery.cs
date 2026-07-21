using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerChecklist;

/// <summary>
/// Fetches the maintenance or fault checklist of a Liftdesk-sourced customer from the Liftdesk API.
/// <paramref name="Kind"/> is "maintenance" or "fault".
/// </summary>
public sealed record GetCustomerChecklistQuery(
    Guid CustomerId,
    string Kind) : IRequest<Result<LiftdeskChecklistDoc>>;
