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
                        o.Stage != OpportunityStage.ClosedWon &&
                        o.Stage != OpportunityStage.ClosedLost)
            .CountAsync(cancellationToken);

        // Use SumAsync directly — result is decimal?, ?? applied in memory
        var pipelineValue = (await _db.Opportunities
            .Where(o => o.ProjectId == projectId && o.Stage != OpportunityStage.ClosedLost)
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
            .Where(o => o.ProjectId == projectId && o.Stage != OpportunityStage.ClosedLost)
            .Select(o => new { o.Stage, o.Value })
            .ToListAsync(cancellationToken);

        var opportunitiesByStage = allOpportunities
            .GroupBy(o => o.Stage)
            .OrderBy(g => g.Key)
            .Select(g => new StageBreakdownDto(g.Key, g.Count(), g.Sum(o => o.Value ?? 0m)))
            .ToList();

        // ── Recent activities (last 10) ───────────────────────────────────────

        var recentRaw = await _db.ContactHistories
            .Include(h => h.Customer)
            .Include(h => h.CreatedByUser)
            .Where(h => h.ProjectId == projectId)
            .OrderByDescending(h => h.ContactedAt)
            .Take(10)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var recentActivities = recentRaw
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
            recentActivities
        );
    }
}
