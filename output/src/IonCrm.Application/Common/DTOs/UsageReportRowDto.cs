namespace IonCrm.Application.Common.DTOs;

/// <summary>
/// One row of the customer usage report — a customer joined to its usage snapshot for a given
/// month. Powers the churn dashboard's usage list. Fields sourced from the Liftdesk summary/plan
/// endpoints; <see cref="LastLoginAt"/> and <see cref="WorkOrderCount"/> stay null/0 until Liftdesk
/// exposes them.
/// </summary>
public record UsageReportRowDto(
    Guid CustomerId,
    string CompanyName,
    string? LegacyId,
    string? Status,
    int SnapshotYear,
    int SnapshotMonth,
    int ElevatorCount,
    int UserCount,
    DateTime? LastLoginAt,
    int MaintenanceCount,
    int FaultCount,
    int PartChangeOfferCount,
    int RevisionOfferCount,
    int AssemblyOfferCount,
    int WorkOrderCount,
    string? PlanTier,
    string? PlanStatus,
    decimal? PlanMonthlyPrice,
    DateTime? ExpirationDate,
    DateTime CapturedAt);
