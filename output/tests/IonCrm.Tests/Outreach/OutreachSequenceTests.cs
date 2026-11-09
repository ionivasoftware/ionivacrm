using FluentAssertions;
using IonCrm.Application.Features.Leads;
using IonCrm.Application.Features.Marketing;
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

    [Fact]
    public void Regions_by_last_scan_pick_never_scanned_then_oldest_and_never_wrap_to_london()
    {
        var gb = GlobalLeadRegions.ByCountry["GB"];
        var t0 = new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc);
        var last = gb.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => t0.AddDays(x.i), StringComparer.OrdinalIgnoreCase); // all scanned, London oldest
        GlobalLeadRegions.Next("GB", 2, last).Should().Equal("London", "Greater Manchester");
        last["London"] = t0.AddDays(100);                                                     // London just scanned → not next
        GlobalLeadRegions.Next("GB", 1, last).Should().Equal("Greater Manchester");
        last.Remove("Kent");                                                                  // never scanned beats every date
        GlobalLeadRegions.Next("GB", 2, last).Should().Equal("Kent", "Greater Manchester");
        GlobalLeadRegions.Next("XX", 1, new Dictionary<string, DateTime>()).Should().Equal("XX");
    }

    [Fact]
    public void Auto_country_rotation_interleaves_by_scanned_share_and_covers_every_region_once_before_cycling()
    {
        var t0 = new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc);
        var state = GlobalLeadRegions.CountryOrder.ToDictionary(c => c, _ => new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, DateTime>> View() =>
            state.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, DateTime>)kv.Value, StringComparer.OrdinalIgnoreCase);
        var total = GlobalLeadRegions.CountryOrder.Sum(c => GlobalLeadRegions.ByCountry[c].Length);
        total.Should().Be(26 + 6 + 24 + 2, "GB, IE, DE, MT catalogue");

        var picks = new List<string>();
        for (var day = 0; day < total; day++)
        {
            var (country, regions) = GlobalLeadRegions.NextAcrossCountries(1, View());
            regions.Should().ContainSingle();
            state[country][regions[0]] = t0.AddDays(day);
            picks.Add($"{country}:{regions[0]}");
        }

        picks.Should().OnlyHaveUniqueItems("one daily run per region covers the whole catalogue exactly once");
        picks[0].Should().Be("GB:London");
        picks[1].Should().Be("IE:Dublin", "after GB's first run every other country has a lower scanned share; catalogue order breaks the tie");
        picks[2].Should().Be("DE:Berlin");
        picks[3].Should().StartWith("MT:");
        picks.Count(p => p.StartsWith("IE:")).Should().Be(6);
        picks.Count(p => p.StartsWith("DE:")).Should().Be(24);
        picks.Count(p => p.StartsWith("MT:")).Should().Be(2);

        var next = GlobalLeadRegions.NextAcrossCountries(1, View());
        (next.Country, next.Regions[0]).Should().Be(("GB", "London"), "steady state: back to the oldest scan");
    }

    // ── Ülke kuralları (09.11.2026: Almanya + Malta) ─────────────────────────────────────────────

    [Fact]
    public void Email_policy_allows_gb_ie_mt_by_default_requires_consent_for_germany_and_can_be_overridden()
    {
        OutreachPolicy.EmailAllowed("GB", null).Should().BeTrue();
        OutreachPolicy.EmailAllowed("uk", null).Should().BeTrue();
        OutreachPolicy.EmailAllowed("IE", null).Should().BeTrue();
        OutreachPolicy.EmailAllowed("MT", null).Should().BeTrue();
        OutreachPolicy.EmailAllowed("DE", null).Should().BeFalse("UWG §7: prior consent even for B2B");
        OutreachPolicy.EmailAllowed("DE", "GB, DE").Should().BeTrue("explicit override after legal advice");
        OutreachPolicy.EmailAllowed("IE", "GB, DE").Should().BeFalse();
        OutreachPolicy.EmailCountries("  ,").Should().BeEquivalentTo(OutreachPolicy.DefaultEmailCountries, "blank config falls back to the default");
    }

    [Fact]
    public void Country_maps_to_template_culture_site_locale_and_time_zone()
    {
        OutreachPolicy.TemplateCulture("DE").Should().Be("de-DE");
        OutreachPolicy.TemplateCulture("AT").Should().Be("de-DE");
        OutreachPolicy.TemplateCulture("MT").Should().Be("en-GB");
        OutreachPolicy.SignupUrl("GB").Should().Be("https://liftdesk.app/uk/signup/");
        OutreachPolicy.SignupUrl("IE").Should().Be("https://liftdesk.app/ie/signup/");
        OutreachPolicy.SignupUrl("DE").Should().Be("https://liftdesk.app/de/signup/");
        OutreachPolicy.SignupUrl("MT").Should().Be("https://liftdesk.app/en/signup/", "Malta is served by the international site");
        OutreachPolicy.TimeZoneId("MT").Should().Be("Europe/Malta");
        OutreachPolicy.TimeZoneId("DE").Should().Be("Europe/Berlin");
        OutreachPolicy.TimeZoneId("IE").Should().Be("Europe/London");
        OutreachPolicy.CountryLabelTr("DE").Should().Be("Almanya");
        OutreachPolicy.ApplySitePlaceholders("<a href=\"{{SignupUrl}}\">x</a> {{SiteUrl}}", "IE").Should().Be("<a href=\"https://liftdesk.app/ie/signup/\">x</a> https://liftdesk.app/ie/");
    }

    [Fact]
    public void Sequence_for_a_country_swaps_the_time_zone_and_only_the_templates_that_exist()
    {
        var baseDef = SequenceDefinition.Default;
        var de = OutreachPolicy.ForCountry(baseDef, "DE", name => name.StartsWith("intro."));
        de.TimeZone.Should().Be("Europe/Berlin");
        de.Steps[0].Template.Should().Be("intro.de-DE.html");
        de.Steps[1].Template.Should().Be("followup-1.en-GB.html", "no German follow-up on disk → English stays, the send never breaks");
        de.Steps.Select(s => s.DelayDays).Should().Equal(0, 4, 9);

        var gb = OutreachPolicy.ForCountry(baseDef, "GB", _ => true);
        gb.TimeZone.Should().Be("Europe/London");
        gb.Steps.Select(s => s.Template).Should().Equal(baseDef.Steps.Select(s => s.Template));
        OutreachPolicy.ReCulture("sequence.json", "de-DE").Should().Be("sequence.json");
    }

    [Theory]
    [InlineData("Müller Aufzüge GmbH & Co. KG", "muller aufzuge")]
    [InlineData("Schulze Aufzugstechnik e.K.", "schulze aufzugstechnik")]
    [InlineData("Berliner Lift UG (haftungsbeschränkt)", "berliner lift")]
    [InlineData("Apex Lifts Ltd", "apex lifts")]
    [InlineData("Island Elevators Limited", "island elevators")]
    public void German_british_and_maltese_legal_forms_are_dropped_from_names(string raw, string expected) =>
        GlobalLeadNormalizer.NormalizeName(raw).Should().Be(expected);

    [Theory]
    [InlineData("+49 30 1234567", "DE", "301234567")]
    [InlineData("030 1234567", "DE", "301234567")]
    [InlineData("+356 2123 4567", "MT", "21234567")]
    [InlineData("2123 4567", "MT", "21234567")]
    [InlineData("+44 20 7946 0958", "GB", "2079460958")]
    public void Phones_become_the_national_significant_number_for_each_country(string raw, string country, string expected) =>
        GlobalLeadNormalizer.NormalizePhone(raw, country).Should().Be(expected);

    private static string? MarketingGlobalFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "marketing", "global");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public void German_templates_exist_render_and_english_ones_carry_country_placeholders()
    {
        var folder = MarketingGlobalFolder();
        folder.Should().NotBeNull("docs/marketing/global ships with the API");
        foreach (var name in new[] { "intro", "followup-1", "followup-2" })
        {
            var de = File.ReadAllText(Path.Combine(folder!, $"{name}.de-DE.html"));
            de.Should().Contain("{{UnsubscribeUrl}}").And.Contain("[Name]").And.Contain("[Ad Soyad]").And.Contain("lang=\"de-DE\"");
            de.Should().NotContain("Dear ").And.NotContain("Kind regards").And.NotContain("liftdesk.app/en/");
            var en = File.ReadAllText(Path.Combine(folder!, $"{name}.en-GB.html"));
            en.Should().NotContain("liftdesk.app/en/", "links come from {{SignupUrl}}/{{SiteUrl}} per country");
            en.Should().Match(x => x.Contains("{{SignupUrl}}") || x.Contains("{{SiteUrl}}"));
        }
        var intro = File.ReadAllText(Path.Combine(folder!, "intro.de-DE.html"));
        IntroMailTemplate.ExtractSubjects(intro).Should().HaveCount(3);
        var rendered = IntroMailTemplate.Render(intro, 0, "Müller Aufzüge GmbH", null, "Ömer Çakmakcı", "support@ioniva.com", null);
        rendered.Subject.Should().StartWith("Ein System");
        rendered.Html.Should().Contain("Guten Tag Müller Aufzüge GmbH,").And.Contain("Ömer Çakmakcı").And.NotContain("<!-- 1:");
        OutreachPolicy.ApplySitePlaceholders(rendered.Html, "DE").Should().Contain("https://liftdesk.app/de/signup/").And.NotContain("{{SignupUrl}}");
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
