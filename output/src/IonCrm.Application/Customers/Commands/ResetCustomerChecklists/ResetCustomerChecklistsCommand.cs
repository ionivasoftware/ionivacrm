using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using MediatR;

namespace IonCrm.Application.Customers.Commands.ResetCustomerChecklists;

/// <summary>
/// Resets the checklist(s) of a Liftdesk-sourced customer to the Liftdesk default (DEMO) template.
/// DESTRUCTIVE: the company's existing customisation is deleted and re-seeded.
/// <paramref name="Kind"/> is "maintenance", "fault" or "both".
/// </summary>
public sealed record ResetCustomerChecklistsCommand(
    Guid CustomerId,
    string Kind = "both") : IRequest<Result<LiftdeskChecklistResetResponse>>;
