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
/// Tests for the EMS zombie purge — DRY-RUN planning path only. The pinned contract: a row is a
/// zombie ONLY when it is live, bare-numeric, and its project holds the matching
/// EMSMIGRATED-{id} marker; genuine un-migrated customers are never candidates.
/// </summary>
public class EmsZombiePurgeServiceTests
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

    private static EmsZombiePurgeService CreateService(ApplicationDbContext db) =>
        new(db, Mock.Of<ILogger<EmsZombiePurgeService>>());

    private static Customer AddCustomer(
        ApplicationDbContext db, Guid projectId, string? legacyId, string name, bool isDeleted = false)
    {
        var c = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = projectId, LegacyId = legacyId,
            CompanyName = name, IsDeleted = isDeleted,
        };
        db.Customers.Add(c);
        return c;
    }

    [Fact]
    public async Task DryRun_FindsOnlyMarkerBackedNumericRows()
    {
        using var db = CreateDb(nameof(DryRun_FindsOnlyMarkerBackedNumericRows));
        var p = Guid.NewGuid();
        db.Projects.Add(new Project { Id = p, Name = "EMS" });
        AddCustomer(db, p, "EMSMIGRATED-176", "Orka Asansör", isDeleted: true); // marker
        AddCustomer(db, p, "176", "Orka Asansör");        // zombi — marker var
        AddCustomer(db, p, "999", "Gerçek Müşteri");      // marker YOK — dokunulmaz
        AddCustomer(db, p, "PC-176", "Lead");             // numeric değil — dokunulmaz
        AddCustomer(db, p, "176", "Silinmiş Kopya", isDeleted: true); // canlı değil — aday değil
        await db.SaveChangesAsync();

        var report = await CreateService(db).PurgeAsync(dryRun: true, CancellationToken.None);

        var z = Assert.Single(report.Zombies);
        Assert.Equal("176", z.LegacyId);
        Assert.Equal("Orka Asansör", z.CompanyName);
        Assert.Equal(1, report.ZombiesDeleted);
        Assert.Equal(0, report.KeptWithChildren);
        // Dry-run yazmadı.
        Assert.Equal(2, await db.Customers.IgnoreQueryFilters().CountAsync(c => c.LegacyId == "176"));
    }

    [Fact]
    public async Task DryRun_ZombieWithChildren_IsKeptAndWarned()
    {
        using var db = CreateDb(nameof(DryRun_ZombieWithChildren_IsKeptAndWarned));
        var p = Guid.NewGuid();
        db.Projects.Add(new Project { Id = p, Name = "EMS" });
        AddCustomer(db, p, "EMSMIGRATED-3", "Vega", isDeleted: true);
        var z = AddCustomer(db, p, "3", "Vega");
        db.ContactHistories.Add(new IonCrm.Domain.Entities.ContactHistory
        {
            Id = Guid.NewGuid(), ProjectId = p, CustomerId = z.Id,
            Type = ContactType.Note, Content = "sonradan girilmiş", ContactedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var report = await CreateService(db).PurgeAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(1, report.ZombiesFound);
        Assert.Equal(0, report.ZombiesDeleted);
        Assert.Equal(1, report.KeptWithChildren);
        Assert.True(report.Zombies.Single().HasChildren);
        Assert.Contains(report.Warnings, w => w.Contains("ÇOCUKLU"));
    }

    [Fact]
    public async Task DryRun_MarkerInDifferentProject_DoesNotMakeZombie()
    {
        using var db = CreateDb(nameof(DryRun_MarkerInDifferentProject_DoesNotMakeZombie));
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        db.Projects.AddRange(new Project { Id = p1, Name = "A" }, new Project { Id = p2, Name = "B" });
        AddCustomer(db, p1, "EMSMIGRATED-5", "X", isDeleted: true);
        AddCustomer(db, p2, "5", "Başka Projede Aynı Numara"); // farklı proje — zombi DEĞİL
        await db.SaveChangesAsync();

        var report = await CreateService(db).PurgeAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(0, report.ZombiesFound);
    }
}
