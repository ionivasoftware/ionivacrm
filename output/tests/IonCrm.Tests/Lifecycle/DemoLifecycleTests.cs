using FluentAssertions;
using IonCrm.Application.Features.Lifecycle;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.Lifecycle;

public class DemoLifecyclePlannerTests
{
    private static readonly LifecycleOptions O = new();
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc); // Thu 10:00Z

    private static LifecycleCustomer Demo(DateTime created, int trialDays = 7, string? country = "GB", string? email = "info@apex.co.uk", CustomerStatus? status = null, bool optOut = false, bool internalAcc = false, string? legacy = "LIFT-42") =>
        new(Guid.NewGuid(), status ?? CustomerStatus.Demo, created, created.AddDays(trialDays), country, email, internalAcc, optOut, legacy);

    private static IReadOnlySet<LifecycleStep> Sent(params LifecycleStep[] s) => s.ToHashSet();

    [Fact]
    public void Welcome_is_due_right_after_signup()
    {
        var c = Demo(Now.AddHours(-1));
        DemoLifecyclePlanner.NextStep(c, Sent(), Now, O).Should().Be(LifecycleStep.Welcome);
    }

    [Fact]
    public void Welcome_is_not_sent_retroactively_to_old_demos_but_checkin_still_is()
    {
        var c = Demo(Now.AddDays(-20), trialDays: 25); // still Demo, created long before the feature
        DemoLifecyclePlanner.NextStep(c, Sent(), Now, O).Should().BeNull("welcome window is 14 days and check-in window has passed");
        var c2 = Demo(Now.AddDays(-5), trialDays: 7);
        DemoLifecyclePlanner.NextStep(c2, Sent(), Now, O).Should().Be(LifecycleStep.Welcome, "within 14 days welcome goes first");
        DemoLifecyclePlanner.NextStep(c2, Sent(LifecycleStep.Welcome), Now, O).Should().Be(LifecycleStep.CheckIn);
    }

    [Fact]
    public void CheckIn_waits_three_days_and_requires_trial_still_running()
    {
        var c = Demo(Now.AddDays(-2));
        DemoLifecyclePlanner.NextStep(c, Sent(LifecycleStep.Welcome), Now, O).Should().BeNull();
        var c3 = Demo(Now.AddDays(-3).AddMinutes(-1));
        DemoLifecyclePlanner.NextStep(c3, Sent(LifecycleStep.Welcome), Now, O).Should().Be(LifecycleStep.CheckIn);
        DemoLifecyclePlanner.NextStep(c3, Sent(LifecycleStep.Welcome, LifecycleStep.CheckIn), Now, O).Should().BeNull();
    }

    [Fact]
    public void TrialEnded_goes_one_day_after_expiry_only_for_passive_short_trials()
    {
        var expiredYesterday = Demo(Now.AddDays(-8), trialDays: 7, status: CustomerStatus.Passive); // expired 1 day ago
        DemoLifecyclePlanner.NextStep(expiredYesterday, Sent(LifecycleStep.Welcome, LifecycleStep.CheckIn), Now, O).Should().Be(LifecycleStep.TrialEnded);
        var expiredHoursAgo = Demo(Now.AddDays(-7).AddHours(2), trialDays: 7, status: CustomerStatus.Passive);
        DemoLifecyclePlanner.NextStep(expiredHoursAgo, Sent(), Now, O).Should().BeNull("one day grace");
        var longAgo = Demo(Now.AddDays(-40), trialDays: 7, status: CustomerStatus.Passive);
        DemoLifecyclePlanner.NextStep(longAgo, Sent(), Now, O).Should().BeNull("older than MaxAgeDays");
        var churnedRealCustomer = Demo(Now.AddDays(-400), trialDays: 365, status: CustomerStatus.Passive);
        DemoLifecyclePlanner.NextStep(churnedRealCustomer, Sent(), Now, O).Should().BeNull("not a short trial");
        var bought = Demo(Now.AddDays(-8), trialDays: 7, status: CustomerStatus.Active);
        DemoLifecyclePlanner.NextStep(bought, Sent(), Now, O).Should().BeNull();
    }

    [Theory]
    [InlineData("TR", "TR kapsam dışı")]
    [InlineData(null, "ülke bilgisi yok")]
    public void Turkish_and_unknown_country_customers_are_out_of_scope(string? country, string reason)
    {
        var c = Demo(Now.AddHours(-1), country: country);
        DemoLifecyclePlanner.IneligibleReason(c, O).Should().Be(reason);
        DemoLifecyclePlanner.NextStep(c, Sent(), Now, O).Should().BeNull();
    }

    [Fact]
    public void Opt_out_internal_no_email_and_non_liftdesk_records_are_skipped()
    {
        DemoLifecyclePlanner.IneligibleReason(Demo(Now, optOut: true), O).Should().Be("abonelikten çıkmış");
        DemoLifecyclePlanner.IneligibleReason(Demo(Now, internalAcc: true), O).Should().Be("iç hesap");
        DemoLifecyclePlanner.IneligibleReason(Demo(Now, email: null), O).Should().Be("e-posta yok");
        DemoLifecyclePlanner.IneligibleReason(Demo(Now, legacy: "REZV-5"), O).Should().Be("Liftdesk kaydı değil");
        DemoLifecyclePlanner.IneligibleReason(Demo(Now), O).Should().BeNull();
    }

    [Theory]
    [InlineData("GB", "en")] [InlineData("IE", "en")] [InlineData("DE", "de")] [InlineData("AT", "de")] [InlineData("FR", "fr")] [InlineData("XX", "en")] [InlineData(null, "en")]
    public void Language_follows_country_with_english_fallback(string? country, string lang) =>
        DemoLifecyclePlanner.LanguageFor(country).Should().Be(lang);

    [Fact]
    public void ResolveLanguage_falls_back_to_english_when_template_folder_missing()
    {
        DemoLifecyclePlanner.ResolveLanguage("DE", l => l == "en").Should().Be("en");
        DemoLifecyclePlanner.ResolveLanguage("DE", l => l is "en" or "de").Should().Be("de");
    }

    [Fact]
    public void Send_window_uses_customer_local_time()
    {
        // 02:00Z: London 02:00 (closed), Sydney 13:00 (open)
        var t = new DateTime(2026, 11, 5, 2, 0, 0, DateTimeKind.Utc);
        DemoLifecyclePlanner.InSendWindow(t, "GB", O).Should().BeFalse();
        DemoLifecyclePlanner.InSendWindow(t, "AU", O).Should().BeTrue();
        DemoLifecyclePlanner.InSendWindow(new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), "GB", O).Should().BeTrue();
    }

    [Fact]
    public void Unsubscribe_token_is_stable_url_safe_and_verifies()
    {
        var id = Guid.NewGuid();
        var t = DemoLifecyclePlanner.UnsubscribeToken(id, "secret-1");
        t.Should().MatchRegex("^[A-Za-z0-9_-]{32}$");
        DemoLifecyclePlanner.UnsubscribeToken(id, "secret-1").Should().Be(t);
        DemoLifecyclePlanner.VerifyUnsubscribeToken(id, "secret-1", t).Should().BeTrue();
        DemoLifecyclePlanner.VerifyUnsubscribeToken(id, "secret-2", t).Should().BeFalse();
        DemoLifecyclePlanner.VerifyUnsubscribeToken(Guid.NewGuid(), "secret-1", t).Should().BeFalse();
    }
}

public class LifecycleTemplateTests
{
    private const string Html = "<!-- 1: Welcome, {{ContactName}} — {{DaysLeft}} days left -->\n<html lang=\"en-GB\"><body><p>Dear [Name],</p><p>{{CompanyName}} until {{TrialEndDate}} ({{DaysLeft}} days). <a href=\"{{PortalUrl}}\">Open</a> <a href=\"{{UnsubscribeUrl}}\">unsubscribe</a> {{SupportEmail}}</p></body></html>";

    [Fact]
    public void Render_fills_placeholders_and_strips_subject_comment()
    {
        var now = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var r = LifecycleTemplate.Render(Html, "Apex Lifts <Ltd>", "Jane", now.AddDays(4).AddHours(2), "GB", "https://portal.liftdesk.app", "support@liftdesk.app", "https://u/x", now);
        r.Subject.Should().Be("Welcome, Jane — 5 days left");
        r.Html.Should().NotContain("<!--").And.Contain("Dear Jane,").And.Contain("Apex Lifts &lt;Ltd&gt;").And.Contain("9 November 2026").And.Contain("(5 days)")
            .And.Contain("https://portal.liftdesk.app").And.Contain("https://u/x").And.Contain("support@liftdesk.app");
    }

    [Fact]
    public void Render_greets_by_company_when_no_contact_and_counts_zero_days_after_expiry()
    {
        var now = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var r = LifecycleTemplate.Render(Html, "Summit Elevators", null, now.AddDays(-2), "IE", "p", "s", "u", now);
        r.Html.Should().Contain("Dear Summit Elevators,").And.Contain("(0 days)");
        r.Subject.Should().Contain("Summit Elevators");
    }

    [Fact]
    public void File_names_cover_every_step()
    {
        foreach (var s in Enum.GetValues<LifecycleStep>()) LifecycleTemplate.FileFor(s).Should().EndWith(".html");
    }
}
