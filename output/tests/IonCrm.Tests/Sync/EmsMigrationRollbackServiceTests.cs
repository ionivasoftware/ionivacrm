using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace IonCrm.Tests.Sync;

/// <summary>
/// Tests for the EMS migration rollback — DRY-RUN planning path only (the execute path uses raw
/// SQL + an advisory lock the InMemory provider cannot run). The critical contract pinned here:
/// retired source rows are located via the marker derived from EACH SOURCE's original EMS
/// LegacyId, NEVER from the target's LIFT id — under name-based matching those ids differ, and
/// the overlapping numeric ranges would otherwise hit a different company's retired row.
/// </summary>
public class EmsMigrationRollbackServiceTests
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

    private static EmsMigrationRollbackService CreateService(ApplicationDbContext db) =>
        new(db, Mock.Of<ILogger<EmsMigrationRollbackService>>());

    private static Customer AddCustomer(
        ApplicationDbContext db, Guid projectId, string? legacyId, string name, bool isDeleted = false)
    {
        var c = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            LegacyId = legacyId,
            CompanyName = name,
            IsDeleted = isDeleted,
        };
        db.Customers.Add(c);
        return c;
    }

    private static RollbackPlan PlanFor(params RollbackPairPlan[] pairs) =>
        new(pairs.ToList(), DateTime.UtcNow.AddMinutes(1));

    [Fact]
    public async Task DryRun_LocatesSourcesBySourceDerivedMarker_NotTargetLiftId()
    {
        using var db = CreateDb(nameof(DryRun_LocatesSourcesBySourceDerivedMarker_NotTargetLiftId));
        var p = Guid.NewGuid();
        db.Projects.Add(new Project { Id = p, Name = "P" });
        AddCustomer(db, p, "LIFT-176", "Orka Asansör");
        // Retired source: EMS id 3 ≠ target's LIFT id 176. The old target-derived lookup
        // (EMSMIGRATED-176) would miss this row entirely.
        AddCustomer(db, p, "EMSMIGRATED-3", "Orka Asansör", isDeleted: true);
        await db.SaveChangesAsync();

        var plan = PlanFor(new RollbackPairPlan(
            "LIFT-176",
            new List<RollbackSourcePlan> { new("3", "Orka Asansör", WasDeleted: false) },
            new List<string>(),
            TargetWasSoftDeleted: false));

        var report = await CreateService(db).RollbackAsync(plan, dryRun: true, CancellationToken.None);

        Assert.Equal(1, report.GroupsProcessed);
        Assert.Equal(0, report.GroupsSkipped);
        Assert.Equal(1, report.SourcesRestored);
        Assert.Equal("3", report.Groups.Single(g => g.Skipped is null).RestoredCanonicalLegacyId);
    }

    [Fact]
    public async Task DryRun_TargetIdCollidingMarkerOfDifferentCompany_IsNotTouched()
    {
        // The disaster scenario the fix exists for: EMSMIGRATED-176 EXISTS but belongs to a
        // DIFFERENT company (EMS id 176 ≠ this pair's source id 3). The rollback must skip the
        // group — never restore the wrong firm's row.
        using var db = CreateDb(nameof(DryRun_TargetIdCollidingMarkerOfDifferentCompany_IsNotTouched));
        var p = Guid.NewGuid();
        db.Projects.Add(new Project { Id = p, Name = "P" });
        AddCustomer(db, p, "LIFT-176", "Orka Asansör");
        AddCustomer(db, p, "EMSMIGRATED-176", "Erlift Asansör", isDeleted: true); // başka firma!
        await db.SaveChangesAsync();

        var plan = PlanFor(new RollbackPairPlan(
            "LIFT-176",
            new List<RollbackSourcePlan> { new("3", "Orka Asansör", WasDeleted: false) },
            new List<string>(),
            TargetWasSoftDeleted: false));

        var report = await CreateService(db).RollbackAsync(plan, dryRun: true, CancellationToken.None);

        Assert.Equal(0, report.GroupsProcessed);
        Assert.Equal(1, report.GroupsSkipped);
        Assert.Contains(report.Warnings, w => w.Contains("EMSMIGRATED-3") && w.Contains("bulunamadı"));
    }

    [Fact]
    public async Task DryRun_PrefixVariantsSharingOneNumericId_BothRestoredByName()
    {
        using var db = CreateDb(nameof(DryRun_PrefixVariantsSharingOneNumericId_BothRestoredByName));
        var p = Guid.NewGuid();
        db.Projects.Add(new Project { Id = p, Name = "P" });
        AddCustomer(db, p, "LIFT-176", "Orka Asansör");
        // "3" and "SAASA-3" both retire to the SAME marker — two DB rows under EMSMIGRATED-3.
        AddCustomer(db, p, "EMSMIGRATED-3", "Orka Asansör", isDeleted: true);
        AddCustomer(db, p, "EMSMIGRATED-3", "Orka Asansör Eski", isDeleted: true);
        await db.SaveChangesAsync();

        var plan = PlanFor(new RollbackPairPlan(
            "LIFT-176",
            new List<RollbackSourcePlan>
            {
                new("3", "Orka Asansör", WasDeleted: false),
                new("SAASA-3", "Orka Asansör Eski", WasDeleted: true),
            },
            new List<string>(),
            TargetWasSoftDeleted: false));

        var report = await CreateService(db).RollbackAsync(plan, dryRun: true, CancellationToken.None);

        Assert.Equal(1, report.GroupsProcessed);
        Assert.Equal(2, report.SourcesRestored);
        // Canonical = the plan-live source, matched by name.
        Assert.Equal("3", report.Groups.Single().RestoredCanonicalLegacyId);
    }

    [Fact]
    public async Task DryRun_MakesNoWrites()
    {
        using var db = CreateDb(nameof(DryRun_MakesNoWrites));
        var p = Guid.NewGuid();
        db.Projects.Add(new Project { Id = p, Name = "P" });
        AddCustomer(db, p, "LIFT-176", "Orka Asansör");
        AddCustomer(db, p, "EMSMIGRATED-3", "Orka Asansör", isDeleted: true);
        await db.SaveChangesAsync();

        await CreateService(db).RollbackAsync(PlanFor(new RollbackPairPlan(
            "LIFT-176",
            new List<RollbackSourcePlan> { new("3", "Orka Asansör", WasDeleted: false) },
            new List<string>(),
            TargetWasSoftDeleted: false)), dryRun: true, CancellationToken.None);

        var source = await db.Customers.IgnoreQueryFilters()
            .SingleAsync(c => c.LegacyId == "EMSMIGRATED-3");
        Assert.True(source.IsDeleted);
    }
}
