using IonCrm.Application.Common.Models;
using IonCrm.Application.Common.Models.ExternalApis;
using MediatR;

namespace IonCrm.Application.Customers.Queries.GetCustomerPlan;

/// <summary>
/// Fetches the subscription plan screen (current plan + selectable plans) of a Liftdesk-sourced
/// customer from the Liftdesk API.
/// </summary>
public sealed record GetCustomerPlanQuery(Guid CustomerId) : IRequest<Result<LiftdeskCompanyPlan>>;
