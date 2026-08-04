namespace IonCrm.Application.Common.Models.ExternalApis;

// Models for the Liftdesk company subscription-plan API (docs/crm-company-plan-api.md).
// Same api/v1/crm surface + per-project Bearer key as the SaaS integration, and — like the checklist
// API — responses are FLAT (no {success,data,message} envelope). Field names are camelCase.

/// <summary>
/// A company's plan screen in one payload: the current subscription plus the plans it can move to.
/// <c>Current</c> is null for legacy tenants that never had a subscription row — the screen must
/// still open and offer to define a licence period first (a PUT would 409).
/// </summary>
public sealed record LiftdeskCompanyPlan(
    int CompanyId,
    LiftdeskCurrentPlan? Current,
    List<LiftdeskAvailablePlan> AvailablePlans,
    /// <summary>
    /// Operator-facing warning, or null. Non-null typically means the tenant has an auto-renewing
    /// iyzico subscription: changing the tier only affects Liftdesk, iyzico keeps charging the old
    /// amount until someone updates it there by hand.
    /// </summary>
    string? Warning);

/// <summary>The tenant's current subscription. Dates are nullable to tolerate incomplete legacy rows.</summary>
public sealed record LiftdeskCurrentPlan(
    Guid PlanId,
    string Name,
    string Tier,             // Standart | Pro | Prime
    string Status,           // Trialing | Active | PendingPayment | Cancelled | Expired
    string? BillingPeriod,   // Monthly | Yearly
    DateTime? StartDate,
    DateTime? EndDate,
    bool AutoRenew);

/// <summary>A plan the company can switch to (only plans currently on sale, in tier order).</summary>
public sealed record LiftdeskAvailablePlan(
    Guid PlanId,
    string Name,
    string Tier,
    decimal PriceMonthly,
    decimal PriceYearly);

/// <summary>
/// Body of PUT …/plan. Exactly one of <see cref="Tier"/> / <see cref="PlanId"/> is required
/// (<see cref="PlanId"/> wins when both are sent). A null <see cref="BillingPeriod"/> keeps the
/// current period.
/// </summary>
public sealed record LiftdeskPlanChangeRequest(
    string? Tier,
    Guid? PlanId,
    string? BillingPeriod);
