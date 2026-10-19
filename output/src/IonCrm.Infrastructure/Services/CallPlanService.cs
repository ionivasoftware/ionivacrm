using System.Text;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.CallPlan;
using IonCrm.Application.Features.SocialPublishing;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// CSO planner orchestration: gathers each project's companies with their contact recency, promised
/// callbacks and latest usage snapshot, hands them to <see cref="CallPlanner"/> and writes the picked
/// calls as Agent appointments. Idempotent per (project, local day); a forced run only tops up slots
/// that are still empty, so re-running never duplicates a company or overflows a day.
/// Config: CallPlan:NewCallsPerDay (5) · FollowUpsPerDay (5) · SatisfactionPerDay (2) ·
/// HorizonBusinessDays (5) · TimeZone (Europe/Istanbul) · slot hours.
/// </summary>
public sealed class CallPlanService : ICallPlanService
{
    private readonly ApplicationDbContext _db;
    private readonly IAppointmentRepository _appointments;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CallPlanService> _logger;

    public CallPlanService(ApplicationDbContext db, IAppointmentRepository appointments, IConfiguration configuration, ILogger<CallPlanService> logger)
    {
        _db = db; _appointments = appointments; _configuration = configuration; _logger = logger;
    }

    public CallPlanQuotas Quotas => new(
        Math.Max(0, _configuration.GetValue("CallPlan:NewCallsPerDay", 5)),
        Math.Max(0, _configuration.GetValue("CallPlan:FollowUpsPerDay", 5)),
        Math.Max(0, _configuration.GetValue("CallPlan:SatisfactionPerDay", 2)));

    private CallPlanSlots Slots => new(
        _configuration.GetValue("CallPlan:NewCallHour", 10),
        _configuration.GetValue("CallPlan:FollowUpHour", 14),
        _configuration.GetValue("CallPlan:SatisfactionHour", 16),
        _configuration.GetValue("CallPlan:SatisfactionMinute", 30),
        _configuration.GetValue("CallPlan:StepMinutes", 15));

    private int HorizonDays => Math.Clamp(_configuration.GetValue("CallPlan:HorizonBusinessDays", 5), 1, 15);

    private TimeZoneInfo Tz => SocialPublishPlanner.ResolveTimeZone(_configuration["CallPlan:TimeZone"]);

    public async Task<List<CallPlanRunResult>> RunAllAsync(bool force, CancellationToken cancellationToken = default)
    {
        var projectIds = await _db.Customers.IgnoreQueryFilters().Where(c => !c.IsDeleted)
            .Select(c => c.ProjectId).Distinct().ToListAsync(cancellationToken);
        // CallPlan:ProjectIds (virgüllü GUID) verilmişse yalnız o projeler planlanır — kotalar proje başınadır.
        var only = (_configuration["CallPlan:ProjectIds"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToHashSet();
        if (only.Count > 0) projectIds = projectIds.Where(only.Contains).ToList();
        var results = new List<CallPlanRunResult>();
        foreach (var pid in projectIds)
        {
            try { results.Add(await RunForProjectAsync(pid, force, cancellationToken)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CallPlan: proje {ProjectId} planlanamadı", pid);
                results.Add(new CallPlanRunResult(pid, DateOnly.FromDateTime(DateTime.UtcNow), 0, false, $"Hata: {ex.Message}"));
            }
        }
        return results;
    }

    public async Task<CallPlanRunResult> RunForProjectAsync(Guid projectId, bool force, CancellationToken cancellationToken = default)
    {
        var tz = Tz;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var planDate = DateOnly.FromDateTime(nowLocal);

        var existingRun = await _appointments.GetRunAsync(projectId, planDate, cancellationToken);
        if (existingRun is not null && !force)
            return new CallPlanRunResult(projectId, planDate, 0, true, existingRun.Summary ?? "Bugün zaten planlandı.");

        var days = CallPlanner.BusinessDays(CallPlanner.StartDay(nowLocal), HorizonDays);
        var fromUtc = LocalMidnightToUtc(days[0], tz);
        var toUtc = LocalMidnightToUtc(days[^1].AddDays(1), tz);

        var candidates = await BuildCandidatesAsync(projectId, planDate, cancellationToken);

        var planned = await _db.Appointments.IgnoreQueryFilters()
            .Where(a => a.ProjectId == projectId && !a.IsDeleted && a.Status == AppointmentStatus.Planned && a.ScheduledAt >= fromUtc && a.ScheduledAt < toUtc)
            .Select(a => new { a.CustomerId, a.Kind, a.ScheduledAt })
            .ToListAsync(cancellationToken);
        var alreadyPlanned = planned.Select(p => p.CustomerId).ToHashSet();
        var perDayKind = planned
            .GroupBy(p => (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(p.ScheduledAt, DateTimeKind.Utc), tz)), p.Kind))
            .ToDictionary(g => g.Key, g => g.Count());

        var picks = CallPlanner.Plan(candidates, days, Quotas, Slots, alreadyPlanned, perDayKind, planDate);

        var rows = picks.Select(p => new Appointment
        {
            ProjectId = projectId, CustomerId = p.CustomerId, Kind = p.Kind, Source = AppointmentSource.Agent, Status = AppointmentStatus.Planned,
            ScheduledAt = TimeZoneInfo.ConvertTimeToUtc(p.Day.ToDateTime(p.Time, DateTimeKind.Unspecified), tz),
            DurationMinutes = 15, Reason = p.Reason, Score = p.Score, AssignedUserId = p.AssignedUserId,
        }).ToList();
        if (rows.Count > 0) await _appointments.AddRangeAsync(rows, cancellationToken);

        var summary = BuildSummary(days, picks, perDayKind, candidates.Count);
        if (existingRun is null)
        {
            await _appointments.AddRunAsync(new CallPlanRun { ProjectId = projectId, PlanDate = planDate, CreatedCount = rows.Count, Summary = summary, RanAt = DateTime.UtcNow }, cancellationToken);
        }
        else
        {
            existingRun.CreatedCount += rows.Count; existingRun.Summary = summary; existingRun.RanAt = DateTime.UtcNow; existingRun.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("CallPlan: proje {ProjectId} — {Count} randevu oluşturuldu ({Days} iş günü, {Candidates} firma)", projectId, rows.Count, days.Count, candidates.Count);
        return new CallPlanRunResult(projectId, planDate, rows.Count, false, summary);
    }

    public async Task<CallPlanRunResult> RebuildForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var tz = Tz;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var days = CallPlanner.BusinessDays(CallPlanner.StartDay(nowLocal), HorizonDays);
        var fromUtc = LocalMidnightToUtc(days[0], tz);
        var toUtc = LocalMidnightToUtc(days[^1].AddDays(1), tz);
        var removed = await _db.Appointments.IgnoreQueryFilters()
            .Where(a => a.ProjectId == projectId && !a.IsDeleted && a.Source == AppointmentSource.Agent && a.Status == AppointmentStatus.Planned && a.ScheduledAt >= fromUtc && a.ScheduledAt < toUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDeleted, true).SetProperty(a => a.UpdatedAt, DateTime.UtcNow), cancellationToken);
        _logger.LogInformation("CallPlan: proje {ProjectId} — {Removed} planlı ajan randevusu kaldırıldı, yeniden planlanıyor", projectId, removed);
        var result = await RunForProjectAsync(projectId, force: true, cancellationToken);
        return result with { Summary = $"{removed} eski planlı randevu kaldırıldı. {result.Summary}" };
    }

    /// <summary>Companies of the project with contact recency, promised callbacks and the latest usage snapshot.</summary>
    private async Task<List<PlanCandidate>> BuildCandidatesAsync(Guid projectId, DateOnly today, CancellationToken ct)
    {
        var customers = await _db.Customers.IgnoreQueryFilters()
            .Where(c => c.ProjectId == projectId && !c.IsDeleted)
            .Select(c => new { c.Id, c.CompanyName, c.Status, c.Label, c.CreatedAt, c.ExpirationDate, c.IsInternal, c.AssignedUserId })
            .ToListAsync(ct);

        var contactAgg = await _db.ContactHistories.IgnoreQueryFilters()
            .Where(h => h.ProjectId == projectId && !h.IsDeleted)
            .GroupBy(h => h.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Last = g.Max(h => h.ContactedAt),
                Count = g.Count(),
                LastSat = g.Where(h => h.Purpose == AppointmentKind.SatisfactionCall).Max(h => (DateTime?)h.ContactedAt),
            })
            .ToDictionaryAsync(x => x.CustomerId, ct);

        // Promised callbacks: the newest contact row that set NextContactAt, unless a later contact already happened.
        var cutoff = today.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var promises = await _db.ContactHistories.IgnoreQueryFilters()
            .Where(h => h.ProjectId == projectId && !h.IsDeleted && h.NextContactAt != null && h.NextContactAt >= cutoff)
            .Select(h => new { h.CustomerId, h.ContactedAt, h.NextContactAt })
            .ToListAsync(ct);
        var promiseByCustomer = promises.GroupBy(p => p.CustomerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.ContactedAt).First());

        // Latest usage snapshot per customer (this or last month).
        var monthKey = today.Year * 100 + today.Month;
        var prevMonth = today.AddMonths(-1); var prevKey = prevMonth.Year * 100 + prevMonth.Month;
        var snapshots = await _db.CustomerUsageSnapshots.IgnoreQueryFilters()
            .Where(s => s.ProjectId == projectId && !s.IsDeleted && (s.SnapshotYear * 100 + s.SnapshotMonth == monthKey || s.SnapshotYear * 100 + s.SnapshotMonth == prevKey))
            .Select(s => new { s.CustomerId, s.SnapshotYear, s.SnapshotMonth, s.LastLoginAt, s.MaintenanceCount, s.FaultCount, s.WorkOrderCount, s.InvoiceCount, s.CollectionCount, s.ElevatorCount, s.UserCount, s.PlanStatus })
            .ToListAsync(ct);
        var usageByCustomer = snapshots.GroupBy(s => s.CustomerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SnapshotYear * 100 + s.SnapshotMonth).First());

        var list = new List<PlanCandidate>(customers.Count);
        foreach (var c in customers)
        {
            contactAgg.TryGetValue(c.Id, out var agg);
            DateTime? next = null;
            if (promiseByCustomer.TryGetValue(c.Id, out var promise) && (agg is null || agg.Last <= promise.ContactedAt))
                next = promise.NextContactAt;
            UsageSignal? usage = null;
            if (usageByCustomer.TryGetValue(c.Id, out var u))
                usage = new UsageSignal(u.LastLoginAt, u.MaintenanceCount, u.FaultCount, u.WorkOrderCount, u.InvoiceCount, u.CollectionCount, u.ElevatorCount, u.UserCount, u.PlanStatus, u.SnapshotYear, u.SnapshotMonth);

            list.Add(new PlanCandidate(c.Id, c.CompanyName, c.Status, c.Label, c.CreatedAt, agg?.Last, agg?.Count ?? 0, agg?.LastSat, next, c.ExpirationDate, c.IsInternal, c.AssignedUserId, usage));
        }
        return list;
    }

    private static string BuildSummary(IReadOnlyList<DateOnly> days, List<PlannedCall> picks, IReadOnlyDictionary<(DateOnly, AppointmentKind), int> existing, int candidates)
    {
        var sb = new StringBuilder();
        sb.Append($"{candidates} firma değerlendirildi; {picks.Count} yeni randevu. ");
        foreach (var d in days)
        {
            int n(AppointmentKind k) => existing.GetValueOrDefault((d, k)) + picks.Count(p => p.Day == d && p.Kind == k);
            sb.Append($"{d:dd.MM} → yeni {n(AppointmentKind.NewCall)}, takip {n(AppointmentKind.FollowUpCall)}, memnuniyet {n(AppointmentKind.SatisfactionCall)}; ");
        }
        return sb.ToString().TrimEnd(' ', ';');
    }

    private static DateTime LocalMidnightToUtc(DateOnly day, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), tz);
}
