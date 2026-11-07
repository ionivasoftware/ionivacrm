using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Tests.Reports;

/// <summary>GET /reports/companies — status × country counts and competitor analysis (<see cref="DashboardRepository.GetCompanyReportAsync"/>).</summary>
public class CompanyReportTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid OtherProject = Guid.NewGuid();

    private static ApplicationDbContext Db()
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(x => x.IsSuperAdmin).Returns(true);
        current.Setup(x => x.ProjectIds).Returns(new List<Guid>());
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            current.Object);
    }

    private static Customer C(string name, CustomerStatus status, string? country = null, string? phone = null,
        bool usesCompetitor = false, Guid? competitorId = null, Guid? project = null, bool isInternal = false, bool deleted = false)
        => new()
        {
            Id = Guid.NewGuid(), ProjectId = project ?? Project, CompanyName = name, Status = status, Country = country, Phone = phone,
            UsesCompetitor = usesCompetitor, CompetitorId = competitorId, IsInternal = isInternal, IsDeleted = deleted,
        };

    private static Domain.Entities.ContactHistory H(Customer c, DateTime at, bool usesCompetitor, Guid? competitorId = null)
        => new() { CustomerId = c.Id, ProjectId = c.ProjectId, Type = ContactType.Call, ContactedAt = at, UsesCompetitor = usesCompetitor, CompetitorId = competitorId };

    private sealed record Seed(Competitor Rival, Customer TrActive, Customer TrLead, Customer GbDemo, Customer UnknownLead, Customer Churned);

    private static async Task<Seed> SeedAsync(ApplicationDbContext db)
    {
        var rival = new Competitor { ProjectId = Project, Name = "Asansör Takip", Website = "https://rakip.example" };
        var deletedRival = new Competitor { ProjectId = Project, Name = "Kapanan Firma", IsDeleted = true };
        db.Competitors.AddRange(rival, deletedRival);

        var trActive = C("TR Aktif", CustomerStatus.Active, "TR", usesCompetitor: true, competitorId: rival.Id);
        var trLead = C("TR Lead", CustomerStatus.Lead, phone: "+90 532 000 00 00");            // ülke telefondan
        var gbDemo = C("GB Demo", CustomerStatus.Demo, "GB", usesCompetitor: true, competitorId: rival.Id);
        var unknownLead = C("Bilinmez", CustomerStatus.Lead, phone: "0555 000 00 00", usesCompetitor: true); // adı verilmedi; ülke yok + ulusal telefon → varsayılan TR
        var churned = C("Kayıp", CustomerStatus.Churned, "TR", usesCompetitor: true, competitorId: deletedRival.Id); // rakip silinmiş → adı yok
        var internalFirm = C("IONIVA", CustomerStatus.Active, "TR", isInternal: true);
        var other = C("Başka Proje", CustomerStatus.Active, "DE", project: OtherProject);
        var deleted = C("Silinmiş", CustomerStatus.Lead, "TR", deleted: true);
        db.Customers.AddRange(trActive, trLead, gbDemo, unknownLead, churned, internalFirm, other, deleted);

        db.ContactHistories.AddRange(
            H(trActive, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), true, rival.Id),
            H(trActive, new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), true, rival.Id),
            H(trActive, new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc), false),           // konu açılmadı → sayılmaz
            H(unknownLead, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), true));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seed(rival, trActive, trLead, gbDemo, unknownLead, churned);
    }

    [Fact]
    public async Task Counts_customers_by_status_and_country_excluding_internal_deleted_and_other_projects()
    {
        await using var db = Db();
        await SeedAsync(db);

        var report = await new DashboardRepository(db).GetCompanyReportAsync(Project);

        report.Totals.Should().Be(new IonCrm.Application.Common.DTOs.StatusCountsDto(Lead: 2, Active: 1, Demo: 1, Churned: 1, Passive: 0, Total: 5));

        report.ByCountry.Select(x => (x.Country, x.Total)).Should().Equal(("TR", 4), ("GB", 1));
        var tr = report.ByCountry.Single(x => x.Country == "TR");
        (tr.Active, tr.Lead, tr.Churned, tr.Demo).Should().Be((1, 2, 1, 0), "the lead with a national phone counts as Turkish");

        report.CountryFromPhone.Should().Be(1, "TR Lead has no country but a +90 phone");
        report.CountryDefaulted.Should().Be(1, "a national phone number tells nothing → home market");
        report.DefaultCountry.Should().Be("TR");
    }

    [Fact]
    public async Task Competitor_analysis_groups_by_vendor_and_puts_unnamed_last()
    {
        await using var db = Db();
        var seed = await SeedAsync(db);

        var report = await new DashboardRepository(db).GetCompanyReportAsync(Project);

        report.UsingCompetitor.Should().Be(4);
        report.UsingCompetitorUnnamed.Should().Be(2, "one record named no vendor, one points at a deleted vendor");

        report.Competitors.Should().HaveCount(2);
        var rival = report.Competitors[0];
        rival.CompetitorId.Should().Be(seed.Rival.Id);
        rival.Name.Should().Be("Asansör Takip");
        rival.Website.Should().Be("https://rakip.example");
        (rival.Total, rival.Active, rival.Demo, rival.Lead).Should().Be((2, 1, 1, 0));
        rival.Countries.Select(c => (c.Country, c.Count)).Should().Equal(("GB", 1), ("TR", 1));

        var unnamed = report.Competitors[1];
        unnamed.CompetitorId.Should().BeNull();
        unnamed.Name.Should().Be(DashboardRepository.UnnamedCompetitor);
        (unnamed.Total, unnamed.Lead, unnamed.Churned).Should().Be((2, 1, 1));
    }

    [Fact]
    public async Task Competitor_customers_carry_the_latest_competitor_contact_and_sort_newest_first()
    {
        await using var db = Db();
        var seed = await SeedAsync(db);

        var report = await new DashboardRepository(db).GetCompanyReportAsync(Project);

        report.CompetitorCustomers.Select(c => c.CompanyName).Should().Equal("TR Aktif", "Bilinmez", "GB Demo", "Kayıp");

        var trActive = report.CompetitorCustomers[0];
        trActive.CustomerId.Should().Be(seed.TrActive.Id);
        trActive.LastContactAt.Should().Be(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), "the later record did not mention a competitor");
        trActive.CompetitorName.Should().Be("Asansör Takip");
        trActive.Status.Should().Be("Active");
        trActive.Country.Should().Be("TR");
        trActive.CountrySource.Should().Be(CountryInference.SourceRecord);

        var unknown = report.CompetitorCustomers[1];
        unknown.CompetitorId.Should().BeNull();
        unknown.Country.Should().Be("TR");
        unknown.CountrySource.Should().Be(CountryInference.SourceDefault);
        unknown.LastContactAt.Should().Be(new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));

        report.CompetitorCustomers[2].LastContactAt.Should().BeNull("no contact record carries the flag");
        report.CompetitorCustomers[3].CompetitorId.Should().BeNull("its vendor was deleted");
    }

    [Fact]
    public async Task Empty_project_returns_zeros_and_empty_lists()
    {
        await using var db = Db();
        await SeedAsync(db);

        var report = await new DashboardRepository(db).GetCompanyReportAsync(Guid.NewGuid());

        report.Totals.Total.Should().Be(0);
        report.ByCountry.Should().BeEmpty();
        report.Competitors.Should().BeEmpty();
        report.CompetitorCustomers.Should().BeEmpty();
        report.UsingCompetitor.Should().Be(0);
    }
}
