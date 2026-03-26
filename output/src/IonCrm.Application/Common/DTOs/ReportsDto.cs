namespace IonCrm.Application.Common.DTOs;

public record ReportsDto(
    int TotalCustomers,
    int NewCustomers,
    int NewLeads,
    int ClosedWon,
    int ClosedLost,
    int TotalContacts,
    int TotalTasks,
    int CompletedTasks,
    decimal PipelineValue,
    IReadOnlyList<DailyActivityDto> DailyActivity,
    IReadOnlyList<ContactTypeBreakdownDto> ContactTypeBreakdown
);

public record DailyActivityDto(string Date, int Contacts, int Tasks);

public record ContactTypeBreakdownDto(string Type, int Count);
