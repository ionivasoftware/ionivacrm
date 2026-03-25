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

    public DashboardRepository(ApplicationDbContext db)
    {
        _db = db;
    }

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

        var pipelineValue = await _db.Opportunities
            .Where(o => o.ProjectId == projectId &&
                        o.Stage != OpportunityStage.ClosedLost)
            .SumAsync(o => (decimal?)(o.Value ?? 0), cancellationToken) ?? 0m;

        // ── Monthly activity (last 6 months) ─────────────────────────────────

        var rawActivity = await _db.ContactHistories
            .Where(h => h.ProjectId == projectId && h.ContactedAt >= sixMonthsAgo)
            .Select(h => new { h.ContactedAt.Year, h.ContactedAt.Month, h.Type })
            .ToListAsync(cancellationToken);

        var monthlyActivity = Enumerable.Range(0, 6)
            .Select(i => monthStart.AddMonths(-5 + i))
            .Select(m =>
            {
                var slice = rawActivity.Where(h => h.Year == m.Year && h.Month == m.Month).ToList();
                return new MonthlyActivityDto(
                    TurkishMonths[m.Month - 1],
                    slice.Count(h => h.Type == ContactType.Call),
                    slice.Count(h => h.Type == ContactType.Meeting),
                    slice.Count(h => h.Type == ContactType.Email)
                );
            })
            .ToList();

        // ── Customers by status ───────────────────────────────────────────────

        var statusGroups = await _db.Customers
            .Where(c => c.ProjectId == projectId)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var customersByStatus = statusGroups
            .Select(g => new StatusBreakdownDto(g.Status, g.Count))
            .ToList();

        // ── Opportunities by stage ────────────────────────────────────────────

        var stageGroups = await _db.Opportunities
            .Where(o => o.ProjectId == projectId &&
                        o.Stage != OpportunityStage.ClosedLost)
            .GroupBy(o => o.Stage)
            .Select(g => new
            {
                Stage = g.Key,
                Count = g.Count(),
                Value = g.Sum(o => (decimal?)(o.Value ?? 0)) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var opportunitiesByStage = stageGroups
            .OrderBy(g => g.Stage)
            .Select(g => new StageBreakdownDto(g.Stage, g.Count, g.Value))
            .ToList();

        // ── Recent activities (last 10) ───────────────────────────────────────

        var recentRaw = await _db.ContactHistories
            .Where(h => h.ProjectId == projectId)
            .OrderByDescending(h => h.ContactedAt)
            .Take(10)
            .Select(h => new
            {
                h.Id,
                h.Type,
                CustomerName = h.Customer.CompanyName,
                h.Subject,
                CreatedByUserName = h.CreatedByUser != null
                    ? h.CreatedByUser.FirstName + " " + h.CreatedByUser.LastName
                    : null,
                h.ContactedAt
            })
            .ToListAsync(cancellationToken);

        var recentActivities = recentRaw
            .Select(h => new RecentActivityDto(
                h.Id.ToString(),
                h.Type,
                h.CustomerName,
                h.Subject,
                h.CreatedByUserName,
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
