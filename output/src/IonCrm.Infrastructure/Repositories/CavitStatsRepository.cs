using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// Counts for the Cavit assistant. Called with a service key, not a user JWT, so there is no tenant
/// context: every query uses <c>IgnoreQueryFilters()</c> and filters soft-delete + project explicitly.
/// Internal/test firms (<c>IsInternal</c>) never count.
/// </summary>
public class CavitStatsRepository : ICavitStatsRepository
{
    private readonly ApplicationDbContext _db;

    public CavitStatsRepository(ApplicationDbContext db) => _db = db;

    /// <summary>Türkiye has been UTC+3 all year since 2016; use tzdata when present.</summary>
    internal static TimeSpan IstanbulOffset(DateTime utc)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul").GetUtcOffset(utc); }
        catch (Exception) { return TimeSpan.FromHours(3); }
    }

    public async Task<List<string>> ProjectNamesAsync(CancellationToken cancellationToken = default)
        => await _db.Projects.IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<CavitStatsDto?> GetAsync(string projectName, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var needle = (projectName ?? string.Empty).Trim().ToLowerInvariant();
        if (needle.Length == 0) return null;

        var projects = await _db.Projects.IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.IsActive })
            .ToListAsync(cancellationToken);
        var project = projects
            .Where(p => p.Name.ToLowerInvariant().Contains(needle))
            .OrderByDescending(p => p.IsActive)
            .FirstOrDefault();
        if (project is null) return null;
        var pid = project.Id;

        // Local (Istanbul) calendar boundaries, expressed back in UTC for the queries.
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var offset = IstanbulOffset(utcNow);
        var local = utcNow + offset;
        var dayStart = DateTime.SpecifyKind(local.Date - offset, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var mondayDelta = ((int)local.DayOfWeek + 6) % 7;
        var weekStart = dayStart.AddDays(-mondayDelta);
        var weekEnd = weekStart.AddDays(7);
        var monthStart = DateTime.SpecifyKind(new DateTime(local.Year, local.Month, 1) - offset, DateTimeKind.Utc);

        var customers = _db.Customers.IgnoreQueryFilters()
            .Where(c => c.ProjectId == pid && !c.IsDeleted && !c.IsInternal);

        var activeCustomers = await customers.CountAsync(c => c.Status == CustomerStatus.Active, cancellationToken);
        var demoCustomers = await customers.CountAsync(c => c.Status == CustomerStatus.Demo, cancellationToken);

        // Latest monthly usage snapshot (synced from Liftdesk) for this project.
        var snaps = _db.CustomerUsageSnapshots.IgnoreQueryFilters()
            .Where(s => s.ProjectId == pid && !s.IsDeleted);
        var latestYm = await snaps
            .Select(s => (int?)(s.SnapshotYear * 100 + s.SnapshotMonth))
            .MaxAsync(cancellationToken);

        var users = 0;
        var loggedIn30d = 0;
        if (latestYm is int ym)
        {
            var latest = snaps.Where(s => s.SnapshotYear * 100 + s.SnapshotMonth == ym);
            users = await (from s in latest
                           join c in customers on s.CustomerId equals c.Id
                           where c.Status == CustomerStatus.Active
                           select s.UserCount).SumAsync(cancellationToken);
            var since = utcNow.AddDays(-30);
            loggedIn30d = await (from s in latest
                                 join c in customers on s.CustomerId equals c.Id
                                 where s.LastLoginAt != null && s.LastLoginAt > since
                                 select s.Id).CountAsync(cancellationToken);
        }

        var appts = _db.Appointments.IgnoreQueryFilters()
            .Where(a => a.ProjectId == pid && !a.IsDeleted && a.Status != AppointmentStatus.Skipped);
        var today = appts.Where(a => a.ScheduledAt >= dayStart && a.ScheduledAt < dayEnd);
        var apptsToday = await today.CountAsync(cancellationToken);
        var apptsTodayDone = await today.CountAsync(a => a.Status == AppointmentStatus.Completed, cancellationToken);

        var demosWeekQ = appts.Where(a => a.Kind == AppointmentKind.Demo && a.ScheduledAt >= weekStart && a.ScheduledAt < weekEnd);
        var demosWeek = await demosWeekQ.CountAsync(cancellationToken);
        var demosWeekDone = await demosWeekQ.CountAsync(a => a.Status == AppointmentStatus.Completed, cancellationToken);
        var demosTodayLeft = await appts.CountAsync(a => a.Kind == AppointmentKind.Demo
            && a.Status != AppointmentStatus.Completed && a.ScheduledAt >= utcNow && a.ScheduledAt < dayEnd, cancellationToken);

        var contacts = _db.ContactHistories.IgnoreQueryFilters()
            .Where(h => h.ProjectId == pid && !h.IsDeleted && h.Type != ContactType.Note);
        var contactsMonth = await contacts.CountAsync(h => h.ContactedAt >= monthStart, cancellationToken);
        var contactsToday = await contacts.CountAsync(h => h.ContactedAt >= dayStart && h.ContactedAt < dayEnd, cancellationToken);

        var opps = _db.Opportunities.IgnoreQueryFilters().Where(o => o.ProjectId == pid && !o.IsDeleted);
        var openOpps = await opps.CountAsync(o => o.Stage == OpportunityStage.YeniArama
            || o.Stage == OpportunityStage.Potansiyel || o.Stage == OpportunityStage.Demo, cancellationToken);
        var demoOpps = await opps.CountAsync(o => o.Stage == OpportunityStage.Demo, cancellationToken);

        return new CavitStatsDto(
            project.Name, activeCustomers, demoCustomers, users, loggedIn30d, latestYm,
            apptsToday, apptsTodayDone, demosWeek, demosWeekDone, demosTodayLeft,
            contactsMonth, contactsToday, openOpps, demoOpps, utcNow);
    }
}
