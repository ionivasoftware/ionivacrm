using FluentAssertions;
using IonCrm.Application.Features.Leads;

namespace IonCrm.Tests.Leads;

public class LeadNormalizerTests
{
    [Theory]
    [InlineData("+90 (212) 555 12 34", "2125551234")]
    [InlineData("0090 532 111 22 33", "5321112233")]
    [InlineData("0532 111 22 33", "5321112233")]
    [InlineData("532-111-2233", "5321112233")]
    [InlineData("212 555 12 34", "2125551234")]
    [InlineData("", "")]
    [InlineData("1234", "")]
    public void NormalizePhone_yields_national_10_digits(string raw, string expected) =>
        LeadNormalizer.NormalizePhone(raw).Should().Be(expected);

    [Theory]
    [InlineData("YILDIZ ASANSÖR SAN. VE TİC. LTD. ŞTİ.", "yildiz asansor")]
    [InlineData("Yıldız Asansör Ltd Şti", "yildiz asansor")]
    [InlineData("Yıldız Asansör A.Ş.", "yildiz asansor")]
    [InlineData("İzmir Lift Mühendislik Hizmetleri", "izmir lift")]
    [InlineData("Çağdaş Asansör & Yürüyen Merdiven", "cagdas asansor yuruyen merdiven")]
    public void NormalizeName_folds_turkish_and_strips_legal_suffixes(string raw, string expected) =>
        LeadNormalizer.NormalizeName(raw).Should().Be(expected);

    [Theory]
    [InlineData("https://www.yildizasansor.com.tr/iletisim", "yildizasansor.com.tr")]
    [InlineData("info@yildizasansor.com", "yildizasansor.com")]
    [InlineData("yildizasansor@gmail.com", null)]
    [InlineData("www.ornek.com", "ornek.com")]
    [InlineData("", null)]
    public void DomainOf_extracts_registrable_host_and_ignores_generic_mail(string raw, string? expected) =>
        LeadNormalizer.DomainOf(raw).Should().Be(expected);

    [Fact]
    public void Fingerprint_is_stable_and_changes_with_identity()
    {
        var a = LeadNormalizer.Fingerprint("yildiz asansor", "2125551234", "yildizasansor.com");
        var b = LeadNormalizer.Fingerprint("yildiz asansor", "2125551234", "yildizasansor.com");
        var c = LeadNormalizer.Fingerprint("yildiz asansor", "2125551235", "yildizasansor.com");
        a.Should().Be(b).And.HaveLength(64);
        a.Should().NotBe(c);
    }

    [Fact]
    public void NameSimilarity_rates_near_identical_names_high()
    {
        LeadNormalizer.NameSimilarity("yildiz asansor", "yildiz asansor").Should().Be(1);
        LeadNormalizer.NameSimilarity("yildiz asansor", "yildiz asansor sistemleri").Should().BeGreaterThan(0.6);
        LeadNormalizer.NameSimilarity("yildiz asansor", "kartal asansor").Should().BeLessThan(0.7);
    }

    [Fact]
    public void Dedupe_catches_phone_domain_name_and_fuzzy_matches_and_flags_possible_ones()
    {
        var known = new List<KnownCompany>
        {
            new(Guid.NewGuid(), "Yıldız Asansör Ltd Şti", "yildiz asansor", "2125551234", "yildizasansor.com", "1234567890", "İstanbul"),
            new(Guid.NewGuid(), "Kartal Lift", "kartal lift", "2169998877", null, null, "İstanbul"),
        };

        LeadDedupe.Evaluate("baska firma", "2125551234", null, null, null, known).IsDuplicate.Should().BeTrue("phone match");
        LeadDedupe.Evaluate("baska firma", null, "yildizasansor.com", null, null, known).IsDuplicate.Should().BeTrue("domain match");
        LeadDedupe.Evaluate("baska firma", null, null, "1234567890", null, known).IsDuplicate.Should().BeTrue("tax number");
        LeadDedupe.Evaluate("yildiz asansor", "5329990000", "other.com", null, "Ankara", known).IsDuplicate.Should().BeTrue("identical normalised name");

        // Only sector-generic words differ → effectively the same brand → duplicate anywhere.
        LeadDedupe.Evaluate("yildiz asansor sistemleri", "5329990001", null, null, "Ankara", known).IsDuplicate.Should().BeTrue("same distinctive name");

        // Extra distinctive word ("ozkan"): same province → duplicate, other province → possible match for a human.
        var fuzzySameCity = LeadDedupe.Evaluate("yildiz ozkan asansor", "5329990001", null, null, "İstanbul", known);
        var fuzzyOtherCity = LeadDedupe.Evaluate("yildiz ozkan asansor", "5329990001", null, null, "Ankara", known);
        fuzzySameCity.IsDuplicate.Should().BeTrue("≥0.86 and same province");
        fuzzyOtherCity.IsDuplicate.Should().BeFalse();
        fuzzyOtherCity.PossibleMatchId.Should().NotBeNull("0.80–0.92 → possible, human decides");
        fuzzyOtherCity.Notes.Should().Contain("Olası eşleşme");

        var fresh = LeadDedupe.Evaluate("mavi asansor", "5321234567", "maviasansor.com", null, "Bursa", known);
        fresh.IsDuplicate.Should().BeFalse();
        fresh.PossibleMatchId.Should().BeNull();
    }

    [Fact]
    public void Cities_rotate_by_least_recently_scanned_in_priority_order()
    {
        LeadAgentCities.Next(3, new HashSet<string>()).Should().Equal("İstanbul", "Ankara", "İzmir");
        LeadAgentCities.Next(2, new HashSet<string> { "istanbul", "ANKARA" }).Should().Equal("İzmir", "Bursa");
        LeadAgentCities.All.Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().BeGreaterThanOrEqualTo(81);
    }
}
