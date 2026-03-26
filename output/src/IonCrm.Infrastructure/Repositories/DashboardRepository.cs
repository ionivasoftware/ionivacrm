using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>Read-only aggregate queries for the dashboard.</summary>
public class DashboardRepository : IDashboardRepository
{
    private readonly ApplicationDbContext _db;

    private static readonly string[] TurkishMonths =
        ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

    public DashboardRepository(ApplicationDbContext db) => _db = db;

    public async Task<DashboardStatsDto> GetStatsAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sixMonthsAgo = monthStart.AddMonths(-5);

        // ── Scalars ──────────────────────────────────────────────────────────

        var totalCustomers = await _db.Customers
            .Where(c => c.ProjectId == projectId)
            .CountAsync(cancellationToken);

        var activeCustomers = await _db.Customers
            .Where(c => c.ProjectId == projectId && c.Status == CustomerStatus.Active)
            .CountAsync(cancellationToken);

        var newLeadsThisMonth = await _db.Customers
            .Where(c => c.ProjectId == projectId &&
                        c.Status == CustomerStatus.Lead &&
                        c.CreatedAt >= monthStart)
            .CountAsync(cancellationToken);

        var openTasks = await _db.CustomerTasks
            .Where(t => t.ProjectId == projectId &&
                        t.Status != IonCrm.Domain.Enums.TaskStatus.Done &&
                        t.Status != IonCrm.Domain.Enums.TaskStatus.Cancelled)
            .CountAsync(cancellationToken);

        var openOpportunities = await _db.Opportunities
            .Where(o => o.ProjectId == projectId &&
                        o.Stage != OpportunityStage.Musteri &&
                        o.Stage != OpportunityStage.Kayip)
            .CountAsync(cancellationToken);

        // Use SumAsync directly — result is decimal?, ?? applied in memory
        var pipelineValue = (await _db.Opportunities
            .Where(o => o.ProjectId == projectId && o.Stage != OpportunityStage.Kayip)
            .SumAsync(o => (decimal?)o.Value, cancellationToken)) ?? 0m;

        // ── Monthly activity (last 6 months) — fetch raw, group in memory ────

        var rawActivity = await _db.ContactHistories
            .Where(h => h.ProjectId == projectId && h.ContactedAt >= sixMonthsAgo)
            .Select(h => new { h.ContactedAt, h.Type })
            .ToListAsync(cancellationToken);

        var monthlyActivity = Enumerable.Range(0, 6)
            .Select(i => monthStart.AddMonths(-5 + i))
            .Select(m =>
            {
                var slice = rawActivity
                    .Where(h => h.ContactedAt.Year == m.Year && h.ContactedAt.Month == m.Month)
                    .ToList();
                return new MonthlyActivityDto(
                    TurkishMonths[m.Month - 1],
                    slice.Count(h => h.Type == ContactType.Call),
                    slice.Count(h => h.Type == ContactType.Meeting),
                    slice.Count(h => h.Type == ContactType.Email)
                );
            })
            .ToList();

        // ── Customers by status — group in memory ────────────────────────────

        var allCustomerStatuses = await _db.Customers
            .Where(c => c.ProjectId == projectId)
            .Select(c => c.Status)
            .ToListAsync(cancellationToken);

        var customersByStatus = allCustomerStatuses
            .GroupBy(s => s)
            .Select(g => new StatusBreakdownDto(g.Key, g.Count()))
            .ToList();

        // ── Opportunities by stage — group in memory ──────────────────────────

        var allOpportunities = await _db.Opportunities
            .Where(o => o.ProjectId == projectId && o.Stage != OpportunityStage.Kayip)
            .Select(o => new { o.Stage, o.Value })
            .ToListAsync(cancellationToken);

        var opportunitiesByStage = allOpportunities
            .GroupBy(o => o.Stage)
            .OrderBy(g => g.Key)
            .Select(g => new StageBreakdownDto(g.Key, g.Count(), g.Sum(o => o.Value ?? 0m)))
            .ToList();

        // ── Recent activities (last 10) ───────────────────────────────────────

        var recentActivities = await FetchRecentActivitiesAsync(projectId, 10, cancellationToken);

        // ── Expiring subscriptions (within next 30 days) ──────────────────────

        var expiryThreshold = now.AddDays(30);
        var expiringRaw = await _db.Customers
            .Where(c => c.ProjectId == projectId &&
                        c.ExpirationDate != null &&
                        c.ExpirationDate > now &&
                        c.ExpirationDate <= expiryThreshold)
            .OrderBy(c => c.ExpirationDate)
            .Select(c => new { c.Id, c.CompanyName, c.ContactName, c.Phone, c.ExpirationDate })
            .ToListAsync(cancellationToken);

        var expiringCustomers = expiringRaw
            .Select(c => new ExpiringCustomerDto(
                c.Id,
                c.CompanyName,
                c.ContactName,
                c.Phone,
                c.ExpirationDate!.Value,
                (int)(c.ExpirationDate.Value.Date - now.Date).TotalDays))
            .ToList();

        return new DashboardStatsDto(
            totalCustomers,
            activeCustomers,
            newLeadsThisMonth,
            openTasks,
            openOpportunities,
            pipelineValue,
            monthlyActivity,
            customersByStatus,
            opportunitiesByStage,
            recentActivities,
            expiringCustomers
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecentActivityDto>> GetRecentActivitiesAsync(
        int count, CancellationToken cancellationToken = default)
    {
        // Global query filter (tenant isolation) is applied automatically
        return await FetchRecentActivitiesAsync(null, count, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReportsDto> GetReportsAsync(
        Guid projectId, DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var endInclusive = endDate.Date.AddDays(1).AddTicks(-1);

        // ── Customers ──────────────────────────────────────────────────────────
        var totalCustomers = await _db.Customers
            .Where(c => c.ProjectId == projectId)
            .CountAsync(cancellationToken);

        var newCustomers = await _db.Customers
            .Where(c => c.ProjectId == projectId &&
                        c.CreatedAt >= startDate && c.CreatedAt <= endInclusive)
            .CountAsync(cancellationToken);

        var newLeads = await _db.Customers
            .Where(c => c.ProjectId == projectId &&
                        c.Status == CustomerStatus.Lead &&
                        c.CreatedAt >= startDate && c.CreatedAt <= endInclusive)
            .CountAsync(cancellationToken);

        // ── Pipeline ───────────────────────────────────────────────────────────
        var closedWon = await _db.Opportunities
            .Where(o => o.ProjectId == projectId &&
                        o.Stage == OpportunityStage.Musteri &&
                        o.UpdatedAt >= startDate && o.UpdatedAt <= endInclusive)
            .CountAsync(cancellationToken);

        var closedLost = await _db.Opportunities
            .Where(o => o.ProjectId == projectId &&
                        o.Stage == OpportunityStage.Kayip &&
                        o.UpdatedAt >= startDate && o.UpdatedAt <= endInclusive)
            .CountAsync(cancellationToken);

        var pipelineValue = (await _db.Opportunities
            .Where(o => o.ProjectId == projectId && o.Stage != OpportunityStage.Kayip)
            .SumAsync(o => (decimal?)o.Value, cancellationToken)) ?? 0m;

        // ── Contacts ───────────────────────────────────────────────────────────
        var rawContacts = await _db.ContactHistories
            .Where(h => h.ProjectId == projectId &&
                        h.ContactedAt >= startDate && h.ContactedAt <= endInclusive)
            .Select(h => new { h.ContactedAt, h.Type })
            .ToListAsync(cancellationToken);

        var totalContacts = rawContacts.Count;

        var contactTypeBreakdown = rawContacts
            .GroupBy(h => h.Type.ToString())
            .Select(g => new ContactTypeBreakdownDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // ── Tasks ─────────────────────────────────────────────────────────────
        var rawTasks = await _db.CustomerTasks
            .Where(t => t.ProjectId == projectId &&
                        t.CreatedAt >= startDate && t.CreatedAt <= endInclusive)
            .Select(t => new { t.Status })
            .ToListAsync(cancellationToken);

        var totalTasks = rawTasks.Count;
        var completedTasks = rawTasks.Count(t => t.Status == IonCrm.Domain.Enums.TaskStatus.Done);

        // ── Daily activity ────────────────────────────────────────────────────
        var days = (int)(endDate.Date - startDate.Date).TotalDays + 1;
        var contactsByDate = rawContacts
            .GroupBy(h => h.ContactedAt.Date)
            .ToDictionary(g => g.Key, g => g.Count());
        var tasksByDate = rawTasks.Count > 0
            ? new Dictionary<DateTime, int>() // tasks don't have contacted_at, skip for now
            : new Dictionary<DateTime, int>();

        var dailyActivity = Enumerable.Range(0, Math.Min(days, 90))
            .Select(i => startDate.Date.AddDays(i))
            .Select(d => new DailyActivityDto(
                d.ToString("dd.MM"),
                contactsByDate.GetValueOrDefault(d, 0),
                0))
            .ToList();

        return new ReportsDto(
            totalCustomers,
            newCustomers,
            newLeads,
            closedWon,
            closedLost,
            totalContacts,
            totalTasks,
            completedTasks,
            pipelineValue,
            dailyActivity,
            contactTypeBreakdown
        );
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<IReadOnlyList<RecentActivityDto>> FetchRecentActivitiesAsync(
        Guid? projectId, int count, CancellationToken cancellationToken)
    {
        var query = _db.ContactHistories
            .Include(h => h.Customer)
            .Include(h => h.CreatedByUser)
            .AsQueryable();

        if (projectId.HasValue)
            query = query.Where(h => h.ProjectId == projectId.Value);

        var recentRaw = await query
            .OrderByDescending(h => h.ContactedAt)
            .Take(count)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return recentRaw
            .Select(h => new RecentActivityDto(
                h.Id.ToString(),
                h.Type,
                h.Customer?.CompanyName ?? string.Empty,
                h.Subject,
                h.CreatedByUser is not null
                    ? $"{h.CreatedByUser.FirstName} {h.CreatedByUser.LastName}".Trim()
                    : null,
                h.ContactedAt))
            .ToList();
    }
}
