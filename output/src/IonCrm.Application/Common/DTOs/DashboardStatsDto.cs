using IonCrm.Domain.Enums;

namespace IonCrm.Application.Common.DTOs;

public record DashboardStatsDto(
    int TotalCustomers,
    int ActiveCustomers,
    int NewLeadsThisMonth,
    int OpenTasks,
    int OpenOpportunities,
    decimal PipelineValue,
    IReadOnlyList<MonthlyActivityDto> MonthlyActivity,
    IReadOnlyList<StatusBreakdownDto> CustomersByStatus,
    IReadOnlyList<StageBreakdownDto> OpportunitiesByStage,
    IReadOnlyList<RecentActivityDto> RecentActivities
);

public record MonthlyActivityDto(string Month, int Calls, int Meetings, int Emails);

public record StatusBreakdownDto(CustomerStatus Status, int Count);

public record StageBreakdownDto(OpportunityStage Stage, int Count, decimal Value);

public record RecentActivityDto(
    string Id,
    ContactType Type,
    string CustomerName,
    string? Subject,
    string? CreatedByUserName,
    DateTime ContactedAt
);
