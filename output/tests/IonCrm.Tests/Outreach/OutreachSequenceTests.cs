using FluentAssertions;
using IonCrm.Application.Features.Leads;
using IonCrm.Application.Features.Outreach;

namespace IonCrm.Tests.OutreachTests;

public class OutreachSequenceTests
{
    private static readonly SequenceDefinition London = SequenceDefinition.Default; // Europe/London 09–17 weekdays

    [Fact]
    public void Parse_reads_steps_window_and_timezone()
    {
        var def = OutreachSequence.Parse(
            "{ \"timeZone\": \"Europe/London\", \"sendWindow\": { \"startHour\": 8, \"endHour\": 18, \"weekdaysOnly\": true }," +
            "  \"steps\": [ { \"template\": \"intro.en-GB.html\", \"delayDays\": 0, \"attachments\": true }, { \"template\": \"followup-1.en-GB.html\", \"delayDays\": 4 }, { \"template\": \"followup-2.en-GB.html\", \"delayDays\": 9 } ] }");
        def.StartHour.Should().Be(8); def.EndHour.Should().Be(18); def.WeekdaysOnly.Should().BeTrue();
        def.Steps.Should().HaveCount(3);
        def.Steps[0].Attachments.Should().BeTrue(); def.Steps[1].Attachments.Should().BeFalse();
        def.Steps[2].DelayDays.Should().Be(9);
    }

    [Fact]
    public void Parse_falls_back_to_defaults_on_empty_or_broken_input()
    {
        OutreachSequence.Parse("{}").Steps.Should().HaveCount(3);
        OutreachSequence.Parse("{ \"steps\": [ { \"template\": \"  \" } ] }").Steps[0].Template.Should().Be("intro.en-GB.html");
    }

    [Fact]
    public void NextSendTime_inside_window_is_unchanged()
    {
        // Wed 2026-10-21 10:30 London (BST, UTC+1) → 09:30Z
        var t = new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc);
        OutreachSequence.NextSendTime(t, London).Should().Be(t);
    }

    [Fact]
    public void NextSendTime_before_window_moves_to_start_hour()
    {
        var t = new DateTime(2026, 10, 21, 5, 0, 0, DateTimeKind.Utc); // 06:00 BST
        OutreachSequence.NextSendTime(t, London).Should().Be(new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc)); // 09:00 BST
    }

    [Fact]
    public void NextSendTime_after_window_moves_to_next_morning()
    {
        var t = new DateTime(2026, 10, 21, 17, 30, 0, DateTimeKind.Utc); // 18:30 BST Wed
        OutreachSequence.NextSendTime(t, London).Should().Be(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NextSendTime_skips_weekend_to_monday()
    {
        var fridayEvening = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc); // Fri 19:00 BST
        OutreachSequence.NextSendTime(fridayEvening, London).Should().Be(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc)); // Mon 09:00 GMT (clocks back 25 Oct)
        var saturday = new DateTime(2026, 10, 24, 11, 0, 0, DateTimeKind.Utc);
        OutreachSequence.NextSendTime(saturday, London).Should().Be(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ScheduleStep_uses_delta_between_step_delays_and_lands_in_window()
    {
        var introSent = new DateTime(2026, 10, 21, 13, 0, 0, DateTimeKind.Utc); // Wed 14:00 BST
        var step1 = OutreachSequence.ScheduleStep(1, introSent, London); // +4 days → Sun 25 Oct → Mon 26 Oct 09:00 GMT
        step1.Should().Be(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc));
        var step2 = OutreachSequence.ScheduleStep(2, step1, London); // +5 days → Sat 31 Oct → Mon 2 Nov 09:00 GMT
        step2.Should().Be(new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void InjectUnsubscribe_replaces_placeholder_or_appends_footer()
    {
        OutreachSequence.InjectUnsubscribe("<a href=\"{{UnsubscribeUrl}}\">x</a>", "https://u/1").Should().Be("<a href=\"https://u/1\">x</a>");
        var html = OutreachSequence.InjectUnsubscribe("<html><body><p>hi</p></body></html>", "https://u/2");
        html.Should().Contain("https://u/2").And.Contain("unsubscribe here").And.EndWith("</body></html>");
        html.IndexOf("unsubscribe here", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("</body>", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsubscribeToken_is_url_safe_and_unique()
    {
        var a = OutreachSequence.NewUnsubscribeToken(); var b = OutreachSequence.NewUnsubscribeToken();
        a.Should().NotBe(b); a.Should().MatchRegex("^[A-Za-z0-9_-]{32}$");
    }

    [Fact]
    public void Regions_rotate_and_wrap_around()
    {
        GlobalLeadRegions.Next("GB", 2, new HashSet<string>()).Should().Equal("London", "Greater Manchester");
        GlobalLeadRegions.Next("GB", 2, new HashSet<string> { "london", "GREATER MANCHESTER" }).Should().Equal("Birmingham and the West Midlands", "Leeds and West Yorkshire");
        var all = GlobalLeadRegions.ByCountry["GB"].ToHashSet(StringComparer.OrdinalIgnoreCase);
        GlobalLeadRegions.Next("GB", 2, all).Should().Equal("London", "Greater Manchester");
        GlobalLeadRegions.Next("XX", 3, new HashSet<string>()).Should().Equal("XX");
    }
}

public class GlobalLeadNormalizerTests
{
    [Theory]
    [InlineData("+44 20 7946 0958", "2079460958")]
    [InlineData("020 7946 0958", "2079460958")]
    [InlineData("0044 161 496 0123", "1614960123")]
    [InlineData("07700 900123", "7700900123")]
    [InlineData("", "")]
    public void NormalizePhone_gb_strips_prefixes(string raw, string expected) =>
        GlobalLeadNormalizer.NormalizePhone(raw, "GB").Should().Be(expected);

    [Theory]
    [InlineData("Apex Lifts Ltd", "apex lifts")]
    [InlineData("APEX LIFTS LIMITED", "apex lifts")]
    [InlineData("Apex Lift Services PLC", "apex lift services")]
    [InlineData("The Northern Lift Company LLP", "northern lift")]
    public void NormalizeName_drops_uk_legal_forms(string raw, string expected) =>
        GlobalLeadNormalizer.NormalizeName(raw).Should().Be(expected);

    [Theory]
    [InlineData("uk", "GB")]
    [InlineData("GB", "GB")]
    [InlineData("ie", "IE")]
    [InlineData("", "GB")]
    [InlineData("United Kingdom", "GB")]
    public void NormalizeCountry_maps_aliases(string raw, string expected) =>
        GlobalLeadNormalizer.NormalizeCountry(raw).Should().Be(expected);

    [Fact]
    public void Fingerprint_differs_per_country()
    {
        GlobalLeadNormalizer.Fingerprint("GB", "apex lifts", null, "apexlifts.co.uk")
            .Should().NotBe(GlobalLeadNormalizer.Fingerprint("IE", "apex lifts", null, "apexlifts.co.uk"));
    }

    [Theory]
    [InlineData("info@apexlifts.co.uk", true)]
    [InlineData("noreply@apexlifts.co.uk", false)]
    [InlineData("webmaster@apexlifts.co.uk", false)]
    [InlineData("not an email", false)]
    [InlineData("a@b", false)]
    [InlineData(null, false)]
    public void IsUsableEmail_filters_junk(string? email, bool expected) =>
        GlobalLeadNormalizer.IsUsableEmail(email).Should().Be(expected);

    [Fact]
    public void Dedupe_matches_same_domain_or_similar_name_across_global_leads()
    {
        var known = new[] { new KnownCompany(Guid.NewGuid(), "Apex Lifts Ltd", "apex lifts", "2079460958", "apexlifts.co.uk", null, "London") };
        LeadDedupe.Evaluate("apex lift services", null, "apexlifts.co.uk", null, "London", known).IsDuplicate.Should().BeTrue();
        LeadDedupe.Evaluate("apex lifts", null, null, null, "Leeds", known).IsDuplicate.Should().BeTrue();
        LeadDedupe.Evaluate("summit elevators", "1614960123", "summitelevators.co.uk", null, "Manchester", known).IsDuplicate.Should().BeFalse();
    }
}
