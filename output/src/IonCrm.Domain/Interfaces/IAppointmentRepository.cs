using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Interfaces;

/// <summary>Calendar appointments (tenant-filtered through the DbContext's global filter).</summary>
public interface IAppointmentRepository
{
    Task<Appointment?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Appointments in [fromUtc, toUtc) with Customer and AssignedUser loaded, ordered by time.</summary>
    Task<List<Appointment>> ListAsync(
        DateTime fromUtc, DateTime toUtc, Guid? projectId, Guid? customerId, Guid? assignedUserId,
        AppointmentStatus? status, AppointmentKind? kind, CancellationToken cancellationToken = default);

    /// <summary>Upcoming + recent appointments of one customer (for the detail page).</summary>
    Task<List<Appointment>> ListForCustomerAsync(Guid customerId, int limit, CancellationToken cancellationToken = default);

    Task AddAsync(Appointment appointment, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<Appointment> appointments, CancellationToken cancellationToken = default);

    Task UpdateAsync(Appointment appointment, CancellationToken cancellationToken = default);

    /// <summary>Latest run per project (status chip on the screen).</summary>
    Task<CallPlanRun?> GetLatestRunAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<CallPlanRun?> GetRunAsync(Guid projectId, DateOnly planDate, CancellationToken cancellationToken = default);

    Task AddRunAsync(CallPlanRun run, CancellationToken cancellationToken = default);
}
