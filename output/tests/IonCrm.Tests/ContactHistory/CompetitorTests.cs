using FluentAssertions;
using IonCrm.API.Controllers;
using IonCrm.Application.ContactHistory;
using IonCrm.Domain.Entities;

namespace IonCrm.Tests.ContactHistory;

public class CompetitorTests
{
    [Theory]
    [InlineData("  Asansör   Dünyası  ", "Asansör Dünyası")]
    [InlineData("X", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeName_trims_collapses_spaces_and_rejects_too_short(string? raw, string? expected) =>
        CompetitorsController.NormalizeName(raw).Should().Be(expected);

    [Fact]
    public void NormalizeName_rejects_over_200_chars() =>
        CompetitorsController.NormalizeName(new string('a', 201)).Should().BeNull();

    [Fact]
    public void Mirror_sets_flag_replaces_named_vendor_and_keeps_old_name_when_none_given()
    {
        var c = new Customer { CompanyName = "Apex" };
        var a = Guid.NewGuid(); var b = Guid.NewGuid();

        CompetitorMirror.Apply(c, new Domain.Entities.ContactHistory { UsesCompetitor = false, CompetitorId = a }).Should().BeFalse("no flag → not discussed");
        c.UsesCompetitor.Should().BeFalse(); c.CompetitorId.Should().BeNull();

        CompetitorMirror.Apply(c, new Domain.Entities.ContactHistory { UsesCompetitor = true }).Should().BeTrue();
        c.UsesCompetitor.Should().BeTrue(); c.CompetitorId.Should().BeNull();

        CompetitorMirror.Apply(c, new Domain.Entities.ContactHistory { UsesCompetitor = true, CompetitorId = a }).Should().BeTrue();
        c.CompetitorId.Should().Be(a);

        CompetitorMirror.Apply(c, new Domain.Entities.ContactHistory { UsesCompetitor = true }).Should().BeFalse("nothing new");
        c.CompetitorId.Should().Be(a);

        CompetitorMirror.Apply(c, new Domain.Entities.ContactHistory { UsesCompetitor = true, CompetitorId = b }).Should().BeTrue();
        c.CompetitorId.Should().Be(b);
    }
}
