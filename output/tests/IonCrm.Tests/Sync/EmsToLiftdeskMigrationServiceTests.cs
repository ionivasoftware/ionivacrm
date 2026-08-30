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
/// pipeline (LegacyId classification, NAME-based target resolution, target grouping,
/// human-decision guards, deletion carry-over, field-copy planning, report arithmetic) with
/// zero writes, which is where all the decision logic lives.
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

    // ── Name normalization ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Elko Asansör San. ve Tic. Ltd. Şti", "elko asansor san ve tic ltd sti")]
    [InlineData("elko asansör san.ve tic.ltd.şti.", "elko asansor san ve tic ltd sti")]
    [InlineData("ERLİFT ASANSÖR", "erlift asansor")]
    [InlineData("  Çağrı-Lift  (İzmir) ", "cagri lift izmir")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_FoldsTurkishCasePunctuationAndWhitespace(string? input, string expected) =>
        Assert.Equal(expected, CompanyNameMatcher.Normalize(input));

    [Theory]
    [InlineData("Mega Asansör", "mega")]
    [InlineData("MEGA ASANSÖR ELEKTRİK SANAYİ VE TİCARET LTD ŞTİ", "mega")]
    [InlineData("Tork Mühendislik asansör inşaat ticaret", "tork")]
    [InlineData("Asansör San. Tic. Ltd. Şti.", "")] // fully generic → empty core, no fallback
    public void Core_StripsGenericTokens(string input, string expected) =>
        Assert.Equal(expected, CompanyNameMatcher.Core(input));

    // ── Matching & classification ────────────────────────────────────────────

    [Fact]
    public async Task DryRun_MatchesNumericSaasaAndEmsPrefixes_ByExactNormalizedName()
    {
        using var db = CreateDb(nameof(DryRun_MatchesNumericSaasaAndEmsPrefixes_ByExactNormalizedName));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Orka Asansör");
        AddCustomer(db, p.Id, "SAASA-4", "ERLİFT ASANSÖR");
        AddCustomer(db, p.Id, "EMS-5", "Vega Lift San. Tic.");
        // Liftdesk ids intentionally DIFFERENT from the EMS ids — names are the join key.
        AddCustomer(db, p.Id, "LIFT-176", "ORKA ASANSOR");
        AddCustomer(db, p.Id, "LIFT-201", "erlift asansör");
        AddCustomer(db, p.Id, "LIFT-315", "VEGA LİFT SAN TİC");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.True(report.DryRun);
        Assert.Equal(3, report.EmsCustomersFound);
        Assert.Equal(3, report.MigratedPairs);
        Assert.Empty(report.Unmatched);
        Assert.Contains(report.Pairs, x => x.EmsLegacyId == "3" && x.TargetLegacyId == "LIFT-176");
        Assert.Contains(report.Pairs, x => x.EmsLegacyId == "SAASA-4" && x.TargetLegacyId == "LIFT-201");
        Assert.Contains(report.Pairs, x => x.EmsLegacyId == "EMS-5" && x.TargetLegacyId == "LIFT-315");
        Assert.All(report.Pairs, x => Assert.Equal("exact-name", x.MatchMethod));
    }

    [Fact]
    public async Task DryRun_CoreNameFallback_MatchesWhenExactDiffers()
    {
        using var db = CreateDb(nameof(DryRun_CoreNameFallback_MatchesWhenExactDiffers));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Mega Asansör");
        AddCustomer(db, p.Id, "LIFT-277", "MEGA ASANSÖR ELEKTRİK SANAYİ VE TİCARET LTD ŞTİ");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("LIFT-277", pair.TargetLegacyId);
        Assert.Equal("core-name", pair.MatchMethod);
    }

    [Fact]
    public async Task DryRun_DuplicateLiveLiftNames_ReportedAsAmbiguous_NeverGuessed()
    {
        using var db = CreateDb(nameof(DryRun_DuplicateLiveLiftNames_ReportedAsAmbiguous_NeverGuessed));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Merkez Asansör");
        AddCustomer(db, p.Id, "LIFT-70", "MERKEZ ASANSÖR");
        AddCustomer(db, p.Id, "LIFT-88", "MERKEZ ASANSÖR");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(0, report.MigratedPairs);
        var u = Assert.Single(report.Unmatched);
        // "Aynı isimde" pins the EXACT-ambiguity reason — a regression falling through to the
        // core index would produce the "Çekirdek isim" reason instead.
        Assert.StartsWith("Aynı isimde", u.Reason);
        Assert.Contains("LIFT-70", u.Reason);
        Assert.Contains("LIFT-88", u.Reason);
    }

    [Fact]
    public async Task DryRun_AmbiguousExactHit_NeverFallsThroughToUniqueLiveCoreCandidate()
    {
        // Mutation-killing test: exact key hits TWO soft-deleted LIFT duplicates (PickUnique →
        // null via the all-deleted-multiple branch) while the core index holds a unique LIVE
        // third row. A fallthrough-to-core regression would silently auto-match that third row —
        // the wrong-firm-guess class the id-based disaster taught us to fear. Correct behavior:
        // stop at the exact stage, report ambiguity, migrate nothing.
        using var db = CreateDb(nameof(DryRun_AmbiguousExactHit_NeverFallsThroughToUniqueLiveCoreCandidate));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Merkez Asansör");
        AddCustomer(db, p.Id, "LIFT-70", "Merkez Asansör", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-88", "MERKEZ ASANSÖR", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-99", "Merkez Asansör Sanayi"); // live, core "merkez" — bait
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(0, report.MigratedPairs);
        var u = Assert.Single(report.Unmatched);
        Assert.StartsWith("Aynı isimde", u.Reason);
    }

    [Fact]
    public async Task DryRun_ExactMatchWins_EvenWhenCoreNamesCollide()
    {
        using var db = CreateDb(nameof(DryRun_ExactMatchWins_EvenWhenCoreNamesCollide));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Daspa Lift");
        AddCustomer(db, p.Id, "LIFT-160", "Daspa Lift San."); // core "daspa lift" — collides
        AddCustomer(db, p.Id, "LIFT-260", "DASPA LİFT");      // exact "daspa lift" — unique, wins
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        // The core index has two "daspa lift" candidates, but the unique EXACT hit resolves
        // first — the core fallback is never consulted.
        var pair = Assert.Single(report.Pairs);
        Assert.Equal("LIFT-260", pair.TargetLegacyId);
        Assert.Equal("exact-name", pair.MatchMethod);
    }

    [Fact]
    public async Task DryRun_CoreCollisionWithoutExactTwin_IsAmbiguous()
    {
        using var db = CreateDb(nameof(DryRun_CoreCollisionWithoutExactTwin_IsAmbiguous));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Emlift Asansör Ltd."); // exact "emlift asansor ltd" — no twin
        AddCustomer(db, p.Id, "LIFT-252", "Emlift Asansör");    // core "emlift"
        AddCustomer(db, p.Id, "LIFT-267", "EMLİFT ASANSÖR SAN"); // core "emlift"
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(0, report.MigratedPairs);
        var u = Assert.Single(report.Unmatched);
        Assert.Contains("emlift", u.Reason);
        Assert.Contains("manuel karar", u.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DryRun_IgnoresOtherSourcesAndAlreadyMigratedRows()
    {
        using var db = CreateDb(nameof(DryRun_IgnoresOtherSourcesAndAlreadyMigratedRows));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "REZV-1", "Rezerval");
        AddCustomer(db, p.Id, "SAASB-2", "Rezerval Legacy");
        AddCustomer(db, p.Id, "PC-9", "Lead");
        AddCustomer(db, p.Id, "EMSMIGRATED-3", "Orka Asansör", isDeleted: true);
        AddCustomer(db, p.Id, null, "Manual Customer");
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör");
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
        AddCustomer(db, p.Id, "3", "Orka Asansör");
        AddCustomer(db, p.Id, "7", "Yok Böyle Firma");
        AddCustomer(db, p.Id, "SAASA-notanumber", "Parse Fail");
        AddCustomer(db, p.Id, "LIFT-176", "ORKA ASANSOR");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.Equal(3, report.EmsCustomersFound);
        Assert.Equal(1, report.MigratedPairs);
        Assert.Equal(2, report.UnmatchedCount);
        Assert.Equal(report.EmsCustomersFound, report.MigratedPairs + report.UnmatchedCount);
        Assert.Contains(report.Unmatched, u => u.LegacyId == "SAASA-notanumber");
        Assert.Contains(report.Unmatched, u => u.LegacyId == "7"
            && u.Reason.Contains("isim eşleşmesi yok"));
    }

    // ── Deletion carry-over & guards ─────────────────────────────────────────

    [Fact]
    public async Task DryRun_DeletedDuplicateBesideLiveSource_DoesNotSoftDeleteTarget()
    {
        // A soft-deleted duplicate EMS row (same company name) must NOT bury the live
        // sibling's archive under a deleted target.
        using var db = CreateDb(nameof(DryRun_DeletedDuplicateBesideLiveSource_DoesNotSoftDeleteTarget));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "SAASA-3", "Orka Asansör", isDeleted: true);
        AddCustomer(db, p.Id, "3", "Orka Asansör");
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör");
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
        AddCustomer(db, p.Id, "SAASA-3", "Orka Asansör", isDeleted: true);
        AddCustomer(db, p.Id, "3", "Orka Asansör", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör");
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
        AddCustomer(db, p.Id, "3", "Orka Asansör");
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör", isDeleted: true);
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
        AddCustomer(db, p.Id, "3", "Orka Asansör", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör", isDeleted: true);
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
        AddCustomer(db, p.Id, "3", "Orka Asansör", mutate: c =>
        {
            c.Code = "MUS-1";
            c.ContactName = "Ali Veli";
            c.Label = CustomerLabel.Potansiyel;
            c.AssignedUserId = user;
            c.ParasutContactId = "999";
        });
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör", mutate: c =>
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
        var target = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.LegacyId == "LIFT-176");
        Assert.Equal("EXISTING", target.Code);
        Assert.Null(target.ContactName);
        Assert.Null(target.Label);
    }

    [Fact]
    public async Task DryRun_DuplicateGroup_CopiesOnlyFromCanonicalLiveSource()
    {
        using var db = CreateDb(nameof(DryRun_DuplicateGroup_CopiesOnlyFromCanonicalLiveSource));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "SAASA-3", "Orka Asansör", isDeleted: true, mutate: c => c.Code = "DUP");
        AddCustomer(db, p.Id, "3", "Orka Asansör", mutate: c => c.Code = "CANON");
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör");
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
        var ems = AddCustomer(db, p.Id, "3", "Orka Asansör");
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör");
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
        AddCustomer(db, p.Id, "3", "Orka Asansör");
        AddCustomer(db, p.Id, "4", "Vega Lift", isDeleted: true);
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör");
        AddCustomer(db, p.Id, "LIFT-315", "Vega Lift");
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        Assert.False(report.Pairs.Single(x => x.EmsLegacyId == "3").EmsWasDeleted);
        Assert.True(report.Pairs.Single(x => x.EmsLegacyId == "4").EmsWasDeleted);
        // Nothing written in dry-run: originals intact.
        Assert.Equal(2, await db.Customers.IgnoreQueryFilters()
            .CountAsync(c => c.LegacyId == "3" || c.LegacyId == "4"));
    }

    [Fact]
    public async Task DryRun_DuplicateLiftRowsSameName_PrefersLiveTarget()
    {
        using var db = CreateDb(nameof(DryRun_DuplicateLiftRowsSameName_PrefersLiveTarget));
        var p = AddProject(db);
        AddCustomer(db, p.Id, "3", "Orka Asansör");
        AddCustomer(db, p.Id, "LIFT-90", "Orka Asansör", isDeleted: true); // dead duplicate
        AddCustomer(db, p.Id, "LIFT-176", "Orka Asansör");                 // live — must win
        await db.SaveChangesAsync();

        var report = await CreateService(db).MigrateAsync(dryRun: true, CancellationToken.None);

        var pair = Assert.Single(report.Pairs);
        Assert.Equal("LIFT-176", pair.TargetLegacyId);
        Assert.False(pair.TargetWasDeleted);
    }
}
