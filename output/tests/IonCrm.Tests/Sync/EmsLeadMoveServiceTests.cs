using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Tests for the EMS lead → Liftdesk project move — DRY-RUN planning path only (the execute
/// path uses raw SQL + an advisory lock the InMemory provider cannot run).
/// </summary>
public class EmsLeadMoveServiceTests
{
    private static ApplicationDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.IsSuperAdmin).Returns(true);
        currentUser.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        return new ApplicationDbContext(options, currentUser.Object);
    }

    private static EmsLeadMoveService CreateService(ApplicationDbContext db) =>
        new(db, Mock.Of<ILogger<EmsLeadMoveService>>());

    private static (Project Source, Project Target) AddProjects(ApplicationDbContext db)
    {
        var s = new Project { Id = Guid.NewGuid(), Name = "EMS" };
        var t = new Project { Id = Guid.NewGuid(), Name = "Liftdesk" };
        db.Projects.AddRange(s, t);
        return (s, t);
    }

    private static Customer AddCustomer(
        ApplicationDbContext db, Guid projectId, string? legacyId, string name,
        bool isDeleted = false, Action<Customer>? mutate = null)
    {
        var c = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = legacyId,
            CompanyName = name,
            IsDeleted = isDeleted,
        };
        mutate?.Invoke(c);
        db.Customers.Add(c);
        return c;
    }

    [Fact]
    public async Task DryRun_SelectsOnlyPcAndNullLegacyIds_FromSourceProject()
    {
        using var db = CreateDb(nameof(DryRun_SelectsOnlyPcAndNullLegacyIds_FromSourceProject));
        var (s, t) = AddProjects(db);
        AddCustomer(db, s.Id, "PC-1106", "Mega Lead");
        AddCustomer(db, s.Id, null, "Manuel Firma");
        AddCustomer(db, s.Id, "PC-9", "Silinmiş Lead", isDeleted: true); // arşiv de taşınır
        AddCustomer(db, s.Id, "176", "EMS Müşterisi");                    // sync kaynağı — kapsam dışı
        AddCustomer(db, s.Id, "EMSMIGRATED-3", "Taşınmış", isDeleted: true);
        AddCustomer(db, t.Id, "PC-77", "Hedefteki Lead");                 // hedefte — dokunulmaz
        await db.SaveChangesAsync();

        var report = await CreateService(db).MoveAsync(s.Id, t.Id, dryRun: true, CancellationToken.None);

        Assert.True(report.DryRun);
        Assert.Equal(3, report.LeadsFound);
        var names = report.Leads.Select(l => l.CompanyName).ToList();
        Assert.Contains("Mega Lead", names);
        Assert.Contains("Manuel Firma", names);
        Assert.Contains("Silinmiş Lead", names);
    }

    [Fact]
    public async Task DryRun_CountsChildren_AndPreservesLabelStatus()
    {
        using var db = CreateDb(nameof(DryRun_CountsChildren_AndPreservesLabelStatus));
        var (s, t) = AddProjects(db);
        var lead = AddCustomer(db, s.Id, "PC-1", "Aktif Lead", mutate: c =>
        {
            c.Label = CustomerLabel.YuksekPotansiyel;
            c.Status = CustomerStatus.Lead;
        });
        db.ContactHistories.Add(new IonCrm.Domain.Entities.ContactHistory
        {
            Id = Guid.NewGuid(), ProjectId = s.Id, CustomerId = lead.Id,
            Type = ContactType.Note, Content = "a", ContactedAt = DateTime.UtcNow,
        });
        db.Opportunities.Add(new Opportunity
        {
            Id = Guid.NewGuid(), ProjectId = s.Id, CustomerId = lead.Id,
            Title = "Fırsat",
        });
        await db.SaveChangesAsync();

        var report = await CreateService(db).MoveAsync(s.Id, t.Id, dryRun: true, CancellationToken.None);

        var row = Assert.Single(report.Leads);
        Assert.Equal(1, row.ContactHistories);
        Assert.Equal(1, row.Opportunities);
        Assert.Equal("YuksekPotansiyel", row.Label);
        Assert.Equal("Lead", row.Status);

        // Dry-run yazmaz: satır hâlâ kaynak projede.
        var db2 = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == lead.Id);
        Assert.Equal(s.Id, db2.ProjectId);
    }

    [Fact]
    public async Task DryRun_FlagsNameCollisions_WithLiveTargetCustomers()
    {
        using var db = CreateDb(nameof(DryRun_FlagsNameCollisions_WithLiveTargetCustomers));
        var (s, t) = AddProjects(db);
        AddCustomer(db, s.Id, "PC-1106", "Mega Asansör");
        AddCustomer(db, s.Id, "PC-2", "Bambaşka Lead");
        AddCustomer(db, t.Id, "LIFT-277", "MEGA ASANSOR");            // canlı — çakışma
        AddCustomer(db, t.Id, "LIFT-9", "Bambaşka Lead", isDeleted: true); // silinmiş — çakışma DEĞİL
        await db.SaveChangesAsync();

        var report = await CreateService(db).MoveAsync(s.Id, t.Id, dryRun: true, CancellationToken.None);

        Assert.Equal(1, report.NameCollisions);
        var mega = report.Leads.Single(l => l.CompanyName == "Mega Asansör");
        Assert.NotNull(mega.NameCollision);
        Assert.Contains("LIFT-277", mega.NameCollision);
        Assert.Null(report.Leads.Single(l => l.CompanyName == "Bambaşka Lead").NameCollision);
    }

    [Fact]
    public async Task SameSourceAndTarget_Throws()
    {
        using var db = CreateDb(nameof(SameSourceAndTarget_Throws));
        var (s, _) = AddProjects(db);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(db).MoveAsync(s.Id, s.Id, dryRun: true, CancellationToken.None));
    }
}
