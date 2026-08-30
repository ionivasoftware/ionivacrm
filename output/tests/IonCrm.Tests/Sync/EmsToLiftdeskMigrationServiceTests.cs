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
/// Tests for the EMS→Liftdesk one-shot data migration — DRY-RUN planning path only.
///
/// The execute path uses raw SQL (child re-pointing UPDATEs) plus a PostgreSQL advisory lock,
/// neither of which the InMemory provider supports; dry-run exercises the entire planning
/// pipeline (LegacyId classification, target grouping, human-decision guards, deletion
/// carry-over, field-copy planning, report arithmetic) with zero writes, which is where all
/// the decision logic lives.
/// </summary>
public class EmsToLiftdeskMigrationServiceTests
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

    private static EmsToLiftdeskMigrationService CreateService(ApplicationDbContext db) =>
        new(db, Mock.Of<ILogger<EmsToLiftdeskMigrationService>>());

    private static Project AddProject(ApplicationDbContext db, string name = "P")
    {
        var p = new Project { Id = Guid.NewGuid(), Name = name };
        db.Projects.Add(p);
        return p;
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

    // ── Matching & classification ────────────────────────────────────────────

    [Fact]
    public async Task DryRun_MatchesNumericSaasaAndEmsPrefixes_ToLiftTargets()
    {
        using var db = CreateDb(nameof(DryRun_MatchesNumericSaasaAndEmsPrefixes_ToLiftTargets));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Ems Plain");
        AddCustomer(db, p.Id, "SAASA-4", "Ems Saasa");
        AddCustomer(db, p.Id, "EMS-5", "Ems Prefixed");
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        AddCustomer(db, p.Id, "LIFT-4", "Lift 4");
        AddCustomer(db, p.Id, "LIFT-5", "Lift 5");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.True(report.DryRun);
        Assert.Equal(3, report.EmsCustomersFound);
        Assert.Equal(3, report.MigratedPairs);
        Assert.Empty(report.Unmatched);
        Assert.Equal(3, report.Pairs.Count);
        Assert.Contains(report.Pairs, x => x.EmsLegacyId == "3" && x.TargetLegacyId == "LIFT-3");
        Assert.Contains(report.Pairs, x => x.EmsLegacyId == "SAASA-4" && x.TargetLegacyId == "LIFT-4");
        Assert.Contains(report.Pairs, x => x.EmsLegacyId == "EMS-5" && x.TargetLegacyId == "LIFT-5");
    }

    [Fact]
    public async Task DryRun_IgnoresOtherSourcesAndAlreadyMigratedRows()
    {
        using var db = CreateDb(nameof(DryRun_IgnoresOtherSourcesAndAlreadyMigratedRows));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "REZV-1", "Rezerval");
        AddCustomer(db, p.Id, "SAASB-2", "Rezerval Legacy");
        AddCustomer(db, p.Id, "PC-9", "Lead");
        AddCustomer(db, p.Id, "EMSMIGRATED-3", "Already Done", isDeleted: true);
        AddCustomer(db, p.Id, null, "Manual Customer");
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(0, report.EmsCustomersFound);
        Assert.Equal(0, report.MigratedPairs);
        Assert.Empty(report.Unmatched);
    }

    [Fact]
    public async Task DryRun_ReportArithmetic_FoundEqualsPairsPlusUnmatched_WithParseFailures()
    {
        using var db = CreateDb(nameof(DryRun_ReportArithmetic_FoundEqualsPairsPlusUnmatched_WithParseFailures));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Matched");
        AddCustomer(db, p.Id, "7", "No Counterpart");
        AddCustomer(db, p.Id, "SAASA-notanumber", "Parse Fail");
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(3, report.EmsCustomersFound);
        Assert.Equal(1, report.MigratedPairs);
        Assert.Equal(2, report.UnmatchedCount);
        Assert.Equal(report.EmsCustomersFound, report.MigratedPairs + report.UnmatchedCount);
        Assert.Contains(report.Unmatched, u => u.LegacyId == "SAASA-notanumber");
        Assert.Contains(report.Unmatched, u => u.LegacyId == "7");
    }

    // ── Deletion carry-over & guards ─────────────────────────────────────────

    [Fact]
    public async Task DryRun_DeletedDuplicateBesideLiveSource_DoesNotSoftDeleteTarget()
    {
        // The exact scenario the adversarial review flagged: a soft-deleted "SAASA-3"
        // duplicate must NOT bury the live "3" row's archive under a deleted target.
        using var db = CreateDb(nameof(DryRun_DeletedDuplicateBesideLiveSource_DoesNotSoftDeleteTarget));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "SAASA-3", "Dup Deleted", isDeleted: true);
        AddCustomer(db, p.Id, "3", "Canonical Live");
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(2, report.MigratedPairs);
        Assert.Equal(0, report.TargetsSoftDeleted);
        Assert.All(report.Pairs, x => Assert.False(x.TargetSoftDeleted));
        Assert.Contains(report.Warnings, w => w.Contains("aynı hedefe eşlendi"));
    }

    [Fact]
    public async Task DryRun_AllSourcesDeleted_CarriesDeletionToTarget_CountedOnce()
    {
        using var db = CreateDb(nameof(DryRun_AllSourcesDeleted_CarriesDeletionToTarget_CountedOnce));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "SAASA-3", "Dup Deleted", isDeleted: true);
        AddCustomer(db, p.Id, "3", "Also Deleted", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(2, report.MigratedPairs);
        // Group-level: exactly ONE target soft-delete even with two deleted sources.
        Assert.Equal(1, report.TargetsSoftDeleted);
        Assert.Equal(1, report.Pairs.Count(x => x.TargetSoftDeleted));
    }

    [Fact]
    public async Task DryRun_LiveSourceWithDeletedTarget_SkipsWholeGroupForHumanDecision()
    {
        using var db = CreateDb(nameof(DryRun_LiveSourceWithDeletedTarget_SkipsWholeGroupForHumanDecision));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Live Ems");
        AddCustomer(db, p.Id, "LIFT-3", "Deleted Lift", isDeleted: true);
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(0, report.MigratedPairs);
        Assert.Single(report.Unmatched);
        Assert.Contains("Manuel karar", report.Unmatched[0].Reason);
    }

    [Fact]
    public async Task DryRun_DeletedSourceWithDeletedTarget_MigratesArchive_NoExtraCarryOver()
    {
        using var db = CreateDb(nameof(DryRun_DeletedSourceWithDeletedTarget_MigratesArchive_NoExtraCarryOver));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Deleted Ems", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-3", "Deleted Lift", isDeleted: true);
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(1, report.MigratedPairs);
        Assert.Equal(0, report.TargetsSoftDeleted); // target already deleted — nothing to carry
        Assert.True(report.Pairs[0].EmsWasDeleted);
        Assert.True(report.Pairs[0].TargetWasDeleted);
    }

    // ── Field-copy planning ──────────────────────────────────────────────────

    [Fact]
    public async Task DryRun_CopiesCrmOnlyFields_OnlyWhereTargetEmpty()
    {
        using var db = CreateDb(nameof(DryRun_CopiesCrmOnlyFields_OnlyWhereTargetEmpty));
        var p = AddProject(db);
        var user = Guid.NewGuid();
        AddCustomer(db, p.Id, "3", "Ems", mutate: c =>
        {
            c.Code = "MUS-1";
            c.ContactName = "Ali Veli";
            c.Label = CustomerLabel.Potansiyel;
            c.AssignedUserId = user;
            c.ParasutContactId = "999";
        });
        AddCustomer(db, p.Id, "LIFT-3", "Lift", mutate: c =>
        {
            c.Code = "EXISTING"; // occupied — must NOT be overwritten
        });
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        var pair = Assert.Single(report.Pairs);
        Assert.DoesNotContain("Code", pair.CopiedFields);
        Assert.Contains("ContactName", pair.CopiedFields);
        Assert.Contains("Label", pair.CopiedFields);
        Assert.Contains("AssignedUserId", pair.CopiedFields);
        Assert.Contains("ParasutContactId", pair.CopiedFields);

        // Dry-run must not have mutated anything.
        var target = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.LegacyId == "LIFT-3");
        Assert.Equal("EXISTING", target.Code);
        Assert.Null(target.ContactName);
        Assert.Null(target.Label);
    }

    [Fact]
    public async Task DryRun_DuplicateGroup_CopiesOnlyFromCanonicalLiveSource()
    {
        using var db = CreateDb(nameof(DryRun_DuplicateGroup_CopiesOnlyFromCanonicalLiveSource));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "SAASA-3", "Dup Deleted", isDeleted: true, mutate: c => c.Code = "DUP");
        AddCustomer(db, p.Id, "3", "Canonical Live", mutate: c => c.Code = "CANON");
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        // Only the canonical (live) source's pair lists copied fields.
        var canonical = report.Pairs.Single(x => x.EmsLegacyId == "3");
        var duplicate = report.Pairs.Single(x => x.EmsLegacyId == "SAASA-3");
        Assert.Contains("Code", canonical.CopiedFields);
        Assert.Empty(duplicate.CopiedFields);
    }

    // ── Child counts & report snapshots ──────────────────────────────────────

    [Fact]
    public async Task DryRun_CountsChildrenPerSource_IncludingSoftDeletedChildren()
    {
        using var db = CreateDb(nameof(DryRun_CountsChildrenPerSource_IncludingSoftDeletedChildren));
        var p = AddProject(db);
        var ems = AddCustomer(db, p.Id, "3", "Ems");
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        db.ContactHistories.Add(new IonCrm.Domain.Entities.ContactHistory
        {
            Id = Guid.NewGuid(), ProjectId = p.Id, CustomerId = ems.Id,
            Type = ContactType.Note, Content = "a", ContactedAt = DateTime.UtcNow,
        });
        db.ContactHistories.Add(new IonCrm.Domain.Entities.ContactHistory
        {
            Id = Guid.NewGuid(), ProjectId = p.Id, CustomerId = ems.Id,
            Type = ContactType.Note, Content = "b", ContactedAt = DateTime.UtcNow,
            IsDeleted = true, // soft-deleted children count too — the archive moves whole
        });
        db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), ProjectId = p.Id, CustomerId = ems.Id,
            Title = "Inv", IssueDate = DateTime.UtcNow, DueDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal(2, pair.ContactHistories);
        Assert.Equal(1, pair.Invoices);
        Assert.Equal(2, report.ContactHistoriesMoved);
        Assert.Equal(1, report.InvoicesMoved);
    }

    [Fact]
    public async Task DryRun_ReportsOriginalDeletionFlags_NotMutatedState()
    {
        using var db = CreateDb(nameof(DryRun_ReportsOriginalDeletionFlags_NotMutatedState));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Live Ems");
        AddCustomer(db, p.Id, "4", "Deleted Ems", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-3", "Lift 3");
        AddCustomer(db, p.Id, "LIFT-4", "Lift 4");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.False(report.Pairs.Single(x => x.EmsLegacyId == "3").EmsWasDeleted);
        Assert.True(report.Pairs.Single(x => x.EmsLegacyId == "4").EmsWasDeleted);
        // Nothing written in dry-run: originals intact.
        Assert.Equal(2, await db.Customers.IgnoreQueryFilters()
            .CountAsync(c => c.LegacyId == "3" || c.LegacyId == "4"));
    }

    [Fact]
    public async Task DryRun_DuplicateLiftRows_PrefersLiveTarget()
    {
        using var db = CreateDb(nameof(DryRun_DuplicateLiftRows_PrefersLiveTarget));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Ems");
        AddCustomer(db, p.Id, "LIFT-3", "Dead Lift", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-3", "Live Lift");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("Live Lift", pair.TargetCompanyName);
        Assert.Contains(report.Warnings, w => w.Contains("Birden fazla LIFT-3"));
    }
}
