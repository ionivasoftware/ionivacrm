namespace IonCrm.Application.Common.DTOs;

/// <summary>
/// Counts for the Cavit desktop assistant (GET /api/v1/cavit/stats). Numbers only — no customer
/// names, contacts or notes ever leave through this endpoint. Days/weeks/months are Europe/Istanbul.
/// </summary>
public sealed record CavitStatsDto(
    string Project,
    int ActiveCustomers,
    int DemoCustomers,
    int Users,
    int LoggedIn30d,
    int? SnapshotYm,
    int ApptsToday,
    int ApptsTodayDone,
    int DemosWeek,
    int DemosWeekDone,
    int DemosTodayLeft,
    int ContactsMonth,
    int ContactsToday,
    int OpenOpps,
    int DemoOpps,
    DateTime GeneratedAt);
