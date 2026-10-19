using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IAppointmentRepository"/>. HTTP callers get the tenant filter; the planner uses the DbContext directly with IgnoreQueryFilters.</summary>
public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly ApplicationDbContext _context;

    public AppointmentRepository(ApplicationDbContext context) => _context = context;

    public Task<Appointment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Appointments.Include(a => a.Customer).Include(a => a.AssignedUser).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<List<Appointment>> ListAsync(
        DateTime fromUtc, DateTime toUtc, Guid? projectId, Guid? customerId, Guid? assignedUserId,
        AppointmentStatus? status, AppointmentKind? kind, CancellationToken cancellationToken = default)
    {
        var q = _context.Appointments.Include(a => a.Customer).Include(a => a.AssignedUser)
            .Where(a => a.ScheduledAt >= fromUtc && a.ScheduledAt < toUtc);
        if (projectId is { } p) q = q.Where(a => a.ProjectId == p);
        if (customerId is { } c) q = q.Where(a => a.CustomerId == c);
        if (assignedUserId is { } u) q = q.Where(a => a.AssignedUserId == u);
        if (status is { } s) q = q.Where(a => a.Status == s);
        if (kind is { } k) q = q.Where(a => a.Kind == k);
        return q.OrderBy(a => a.ScheduledAt).ThenBy(a => a.Customer.CompanyName).ToListAsync(cancellationToken);
    }

    public Task<List<Appointment>> ListForCustomerAsync(Guid customerId, int limit, CancellationToken cancellationToken = default) =>
        _context.Appointments.Include(a => a.AssignedUser).Include(a => a.Customer)
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.Status == AppointmentStatus.Planned).ThenByDescending(a => a.ScheduledAt)
            .Take(limit).ToListAsync(cancellationToken);

    public async Task AddAsync(Appointment appointment, CancellationToken cancellationToken = default)
    {
        await _context.Appointments.AddAsync(appointment, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<Appointment> appointments, CancellationToken cancellationToken = default)
    {
        await _context.Appointments.AddRangeAsync(appointments, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default)
    {
        appointment.UpdatedAt = DateTime.UtcNow;
        if (_context.Entry(appointment).State == EntityState.Detached) _context.Appointments.Update(appointment);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<CallPlanRun?> GetLatestRunAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        _context.CallPlanRuns.IgnoreQueryFilters().Where(r => r.ProjectId == projectId && !r.IsDeleted)
            .OrderByDescending(r => r.PlanDate).ThenByDescending(r => r.RanAt).FirstOrDefaultAsync(cancellationToken);

    public Task<CallPlanRun?> GetRunAsync(Guid projectId, DateOnly planDate, CancellationToken cancellationToken = default) =>
        _context.CallPlanRuns.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.ProjectId == projectId && r.PlanDate == planDate && !r.IsDeleted, cancellationToken);

    public async Task AddRunAsync(CallPlanRun run, CancellationToken cancellationToken = default)
    {
        await _context.CallPlanRuns.AddAsync(run, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
