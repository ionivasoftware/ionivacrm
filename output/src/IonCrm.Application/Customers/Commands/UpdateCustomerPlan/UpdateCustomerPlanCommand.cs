using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using MediatR;

namespace IonCrm.Application.Customers.Commands.UpdateCustomerPlan;

/// <summary>
/// Switches a Liftdesk-sourced customer's subscription tier. Exactly one of <paramref name="Tier"/>
/// / <paramref name="PlanId"/> must be supplied (PlanId wins when both are). A null
/// <paramref name="BillingPeriod"/> keeps the current period.
///
/// Feature gating changes immediately; the licence PERIOD and STATUS are untouched — an expired
/// tenant stays expired (extend-expiration is a separate operation).
/// </summary>
public sealed record UpdateCustomerPlanCommand(
    Guid CustomerId,
    string? Tier,
    Guid? PlanId,
    string? BillingPeriod) : IRequest<Result<LiftdeskCompanyPlan>>;
