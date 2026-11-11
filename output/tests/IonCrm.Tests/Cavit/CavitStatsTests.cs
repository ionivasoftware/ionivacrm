using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Tests.Cavit;

/// <summary>GET /api/v1/cavit/stats — counts for the Cavit desktop assistant (<see cref="CavitStatsRepository"/>).</summary>
public class CavitStatsTests
{
    // Wed 11 Nov 2026 20:30 UTC = 23:30 in Istanbul. Week (Mon) starts 9 Nov 00:00 local = 8 Nov 21:00 UTC.
    private static readonly DateTime Now = new(2026, 11, 11, 20, 30, 0, DateTimeKind.Utc);
    private static DateTime Utc(int d, int h, int m = 0) => new(2026, 11, d, h, m, 0, DateTimeKind.Utc);

    private static ApplicationDbContext Db(bool superAdmin = false)
    {
        // A service-key call has no user: not super admin, no projects. The repository must not rely on the tenant filter.
        var current = new Mock<ICurrentUserService>();
        current.Setup(x => x.IsSuperAdmin).Returns(superAdmin);
        current.Setup(x => x.ProjectIds).Returns(new List<Guid>());
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            current.Object);
    }

    private static async Task<(Guid lift, Guid other)> SeedAsync(ApplicationDbContext db)
    {
        var lift = new Project { Name = "Liftdesk" };
        var other = new Project { Name = "RezervAl" };
        var gone = new Project { Name = "Liftdesk Eski", IsDeleted = true };
        db.Projects.AddRange(lift, other, gone);

        Customer C(CustomerStatus s, Guid? p = null, bool internalFirm = false, bool deleted = false) => new()
        { ProjectId = p ?? lift.Id, CompanyName = "x", Status = s, IsInternal = internalFirm, IsDeleted = deleted };
        var a1 = C(CustomerStatus.Active); var a2 = C(CustomerStatus.Active); var demo = C(CustomerStatus.Demo);
        var internalFirm = C(CustomerStatus.Active, internalFirm: true);
        var deleted = C(CustomerStatus.Active, deleted: true);
        var otherFirm = C(CustomerStatus.Active, other.Id);
        db.Customers.AddRange(a1, a2, demo, internalFirm, deleted, otherFirm);

        CustomerUsageSnapshot S(Customer c, int month, int users, DateTime? login = null) => new()
        { ProjectId = c.ProjectId, CustomerId = c.Id, SnapshotYear = 2026, SnapshotMonth = month, UserCount = users, LastLoginAt = login };
        db.CustomerUsageSnapshots.AddRange(
            S(a1, 10, 7), S(a1, 11, 12, Now.AddDays(-2)), S(a2, 11, 5, Now.AddDays(-45)),
            S(internalFirm, 11, 99, Now), S(demo, 11, 3, Now), S(otherFirm, 11, 50, Now));

        Appointment A(AppointmentKind k, AppointmentStatus st, DateTime at, Guid? p = null, bool del = false) => new()
        { ProjectId = p ?? lift.Id, CustomerId = demo.Id, Kind = k, Status = st, ScheduledAt = at, IsDeleted = del };
        db.Appointments.AddRange(
            A(AppointmentKind.Demo, AppointmentStatus.Completed, Utc(11, 8)),        // today, done
            A(AppointmentKind.Demo, AppointmentStatus.Planned, Utc(11, 20, 45)),     // today 23:45 local, still ahead
            A(AppointmentKind.NewCall, AppointmentStatus.Skipped, Utc(11, 9)),       // skipped → never counts
            A(AppointmentKind.Meeting, AppointmentStatus.Planned, Utc(11, 21, 30)),  // 00:30 local tomorrow → not today
            A(AppointmentKind.Demo, AppointmentStatus.Planned, Utc(8, 21, 30)),      // Mon 00:30 local → this week
            A(AppointmentKind.Demo, AppointmentStatus.Planned, Utc(8, 20, 30)),      // Sun 23:30 local → last week
            A(AppointmentKind.Demo, AppointmentStatus.Planned, Utc(11, 10), other.Id),
            A(AppointmentKind.Demo, AppointmentStatus.Planned, Utc(11, 10), del: true));

        // Tam ad: `ContactHistory` burada IonCrm.Application.ContactHistory ad alanıyla çakışıyor.
        Domain.Entities.ContactHistory H(ContactType t, DateTime at) => new() { ProjectId = lift.Id, CustomerId = a1.Id, Type = t, ContactedAt = at };
        db.ContactHistories.AddRange(
            H(ContactType.Call, Utc(11, 10)), H(ContactType.Note, Utc(11, 10)),
            H(ContactType.Meeting, Utc(2, 10)),
            H(ContactType.Call, Utc(10, 22)),   // 10th 22:00 UTC = 11th 01:00 local → today
            H(ContactType.Call, Utc(10, 20)));  // 10th 23:00 local → this month, not today

        Opportunity O(OpportunityStage s) => new() { ProjectId = lift.Id, CustomerId = a1.Id, Title = "o", Stage = s };
        db.Opportunities.AddRange(O(OpportunityStage.YeniArama), O(OpportunityStage.Demo), O(OpportunityStage.Musteri), O(OpportunityStage.Kayip));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (lift.Id, other.Id);
    }

    [Fact]
    public async Task Counts_one_project_in_istanbul_time_excluding_internal_deleted_and_skipped()
    {
        await using var db = Db();
        await SeedAsync(db);

        var s = await new CavitStatsRepository(db).GetAsync("liftdesk", Now);

        s.Should().NotBeNull();
        s!.Project.Should().Be("Liftdesk");
        (s.ActiveCustomers, s.DemoCustomers).Should().Be((2, 1));
        s.Users.Should().Be(17, "latest month only, active non-internal firms only (12 + 5)");
        s.LoggedIn30d.Should().Be(2, "demo firm's snapshot counts for logins, the internal one does not");
        s.SnapshotYm.Should().Be(202611);
        (s.ApptsToday, s.ApptsTodayDone).Should().Be((2, 1));
        (s.DemosWeek, s.DemosWeekDone, s.DemosTodayLeft).Should().Be((3, 1, 1));
        (s.ContactsMonth, s.ContactsToday).Should().Be((4, 2), "notes never count");
        (s.OpenOpps, s.DemoOpps).Should().Be((2, 1));
    }

    [Fact]
    public async Task Unknown_project_returns_null_and_names_are_listed()
    {
        await using var db = Db();
        await SeedAsync(db);
        var repo = new CavitStatsRepository(db);

        (await repo.GetAsync("ems", Now)).Should().BeNull();
        (await repo.GetAsync("  ", Now)).Should().BeNull();
        (await repo.ProjectNamesAsync()).Should().Equal("Liftdesk", "RezervAl");
    }

    [Fact]
    public async Task Empty_project_has_zero_counts_and_no_snapshot_month()
    {
        await using var db = Db();
        db.Projects.Add(new Project { Name = "Liftdesk" });
        await db.SaveChangesAsync();

        var s = await new CavitStatsRepository(db).GetAsync("Liftdesk", Now);

        s!.Users.Should().Be(0);
        s.SnapshotYm.Should().BeNull();
        s.ActiveCustomers.Should().Be(0);
    }
}
