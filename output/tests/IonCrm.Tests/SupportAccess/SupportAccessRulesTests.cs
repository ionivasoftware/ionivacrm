using FluentAssertions;
using IonCrm.Application.Features.SupportAccess;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.SupportAccess;

public class SupportAccessRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static SupportAccessRequest Req(SupportAccessStatus status = SupportAccessStatus.Pending, int hours = 2, DateTime? responded = null, DateTime? linkExpires = null) => new()
    {
        CustomerId = CustomerId, EmsUserId = "u-1", Status = status, ValidHours = hours, RequestedAt = Now.AddHours(-1),
        LinkExpiresAt = linkExpires ?? Now.AddDays(6), RespondedAt = responded, ExpiresAt = responded?.AddHours(hours),
    };

    [Fact]
    public void Token_is_url_safe_and_unique()
    {
        var a = SupportAccessRules.NewToken(); var b = SupportAccessRules.NewToken();
        a.Should().MatchRegex("^[A-Za-z0-9_-]{32}$"); a.Should().NotBe(b);
    }

    [Theory] [InlineData(null, 2)] [InlineData(0, 1)] [InlineData(4, 4)] [InlineData(99, 24)]
    public void Hours_default_to_two_and_are_clamped(int? input, int expected) => SupportAccessRules.ClampHours(input).Should().Be(expected);

    [Fact]
    public void Approval_opens_a_window_counted_from_the_answer()
    {
        var r = Req();
        SupportAccessRules.Respond(r, approve: true, Now).Should().BeTrue();
        r.Status.Should().Be(SupportAccessStatus.Approved); r.RespondedAt.Should().Be(Now); r.ExpiresAt.Should().Be(Now.AddHours(2));
        SupportAccessRules.IsActive(r, Now.AddMinutes(119)).Should().BeTrue();
        SupportAccessRules.IsActive(r, Now.AddMinutes(121)).Should().BeFalse();
        SupportAccessRules.WhyCannotLogin(r, CustomerId, "u-1", Now.AddHours(1)).Should().BeNull();
        SupportAccessRules.WhyCannotLogin(r, CustomerId, "u-1", Now.AddHours(3)).Should().Contain("İzin süresi doldu");
    }

    [Fact]
    public void Decline_and_cancel_block_login_and_the_link_cannot_be_answered_twice()
    {
        var r = Req();
        SupportAccessRules.Respond(r, approve: false, Now).Should().BeTrue();
        r.Status.Should().Be(SupportAccessStatus.Declined); r.ExpiresAt.Should().BeNull();
        SupportAccessRules.WhyCannotLogin(r, CustomerId, "u-1", Now).Should().Be("Müşteri destek erişimine izin vermedi.");
        SupportAccessRules.Respond(r, approve: true, Now).Should().BeFalse("already answered");
        SupportAccessRules.WhyCannotRespond(r, Now).Should().Be("Bu istek zaten reddedilmiş.");

        var cancelled = Req(SupportAccessStatus.Cancelled);
        SupportAccessRules.WhyCannotLogin(cancelled, CustomerId, "u-1", Now).Should().Be("Bu isteğin yerini yeni bir istek aldı.");
    }

    [Fact]
    public void Pending_and_stale_requests_do_not_allow_login()
    {
        SupportAccessRules.WhyCannotLogin(Req(), CustomerId, "u-1", Now).Should().Be("Müşteri izin isteğini henüz yanıtlamadı.");
        var stale = Req(linkExpires: Now.AddMinutes(-1));
        SupportAccessRules.IsPending(stale, Now).Should().BeFalse();
        SupportAccessRules.WhyCannotLogin(stale, CustomerId, "u-1", Now).Should().Contain("süresi doldu");
        SupportAccessRules.WhyCannotRespond(stale, Now).Should().Be("Bu isteğin süresi dolmuş.");
        SupportAccessRules.Respond(stale, true, Now).Should().BeFalse();
    }

    [Fact]
    public void Consent_is_bound_to_the_customer_and_user()
    {
        var r = Req(SupportAccessStatus.Approved, responded: Now);
        SupportAccessRules.WhyCannotLogin(r, Guid.NewGuid(), "u-1", Now).Should().Be("İzin isteği bu müşteri/kullanıcıya ait değil.");
        SupportAccessRules.WhyCannotLogin(r, CustomerId, "u-2", Now).Should().Be("İzin isteği bu müşteri/kullanıcıya ait değil.");
    }

    [Theory]
    [InlineData(null, null, null, null, "tr", SupportAccessRules.SourceDefault)]
    [InlineData("en", null, null, null, "en", SupportAccessRules.SourceFirm)]
    [InlineData("tr", "MT", "+356 2122 3344", null, "tr", SupportAccessRules.SourceFirm)]   // firm language beats country and phone
    [InlineData("de-DE", null, null, null, "en", SupportAccessRules.SourceFirm)]            // no German mail yet → English
    [InlineData(null, "MT", null, null, "en", SupportAccessRules.SourceCountry)]
    [InlineData(null, "tr", "+356 2122 3344", null, "tr", SupportAccessRules.SourceCountry)]
    [InlineData(null, null, "+356 2122 3344", null, "en", SupportAccessRules.SourcePhone)]
    [InlineData(null, null, "+90 555 111 22 33", null, "tr", SupportAccessRules.SourcePhone)]
    [InlineData(null, null, "0090 555 111 22 33", null, "tr", SupportAccessRules.SourcePhone)]
    [InlineData(null, null, "0555 111 22 33", null, "tr", SupportAccessRules.SourceDefault)]  // national format says nothing
    [InlineData(null, null, "+1", null, "tr", SupportAccessRules.SourceDefault)]              // too short to trust
    [InlineData("en", "MT", "+356 2122 3344", "tr", "tr", SupportAccessRules.SourceOperator)] // operator's pick wins
    [InlineData(null, null, null, "EN-gb", "en", SupportAccessRules.SourceOperator)]
    [InlineData(null, null, null, "fr", "tr", SupportAccessRules.SourceDefault)]              // unknown pick ignored
    public void Mail_language_prefers_operator_then_firm_language_then_country_then_phone(string? firm, string? country, string? phone, string? requested, string lang, string source)
    {
        var choice = SupportAccessRules.ResolveLanguage(firm, country, phone, requested);
        choice.Language.Should().Be(lang);
        choice.Source.Should().Be(source);
    }

    [Theory] [InlineData("tr", "tr")] [InlineData("EN", "en")] [InlineData("en-GB", "en")] [InlineData("de", null)] [InlineData("", null)] [InlineData(null, null)]
    public void Only_mail_languages_normalize(string? value, string? expected) => SupportAccessRules.NormalizeMailLanguage(value).Should().Be(expected);
}

public class SupportAccessMailTests
{
    private static SupportAccessMail.Context Ctx(string lang) => new(lang, "Apex <Lifts> Ltd", "Jane", "Jane Carter", "support@ioniva.com", "Bakım listesi boş görünüyor, ekran kontrolü", 2, "https://crm/api/v1/support-access/tok");

    [Fact]
    public void Request_mail_is_turkish_or_english_escapes_html_and_links_only_to_the_review_page()
    {
        var tr = SupportAccessMail.RenderRequest(Ctx("tr"));
        tr.Subject.Should().StartWith("LiftDesk destek erişimi için izin isteği");
        tr.Html.Should().Contain("Sayın Jane").And.Contain("Apex &lt;Lifts&gt; Ltd").And.Contain("Jane Carter").And.Contain("2 saat").And.Contain("https://crm/api/v1/support-access/tok")
            .And.NotContain("/approve").And.NotContain("{{");
        var en = SupportAccessMail.RenderRequest(Ctx("en"));
        en.Subject.Should().StartWith("Permission request for LiftDesk support access");
        en.Html.Should().Contain("Dear Jane").And.Contain("2 hours").And.Contain("support@ioniva.com").And.Contain("The LiftDesk support team is asking");
    }

    [Fact]
    public void Customer_facing_content_names_only_the_support_team()
    {
        // The operator's identity stays in the CRM (request row + timeline); the mail and pages speak for the team.
        var tr = SupportAccessMail.RenderRequest(Ctx("tr")).Html;
        tr.Should().Contain("LiftDesk destek ekibi").And.Contain("support@ioniva.com").And.NotContain("Ömer").And.NotContain("omer@");
        var page = SupportAccessMail.RenderReviewPage(Ctx("en"), "https://crm/x/approve", "https://crm/x/decline");
        page.Should().Contain("The LiftDesk support team is asking").And.Contain("support@ioniva.com").And.NotContain("Ömer");
        SupportAccessMail.RenderResultPage("en", "Permission granted", "Thank you.", "support@ioniva.com").Should().Contain("LiftDesk Support by IONIVA").And.Contain("support@ioniva.com");
    }

    [Fact]
    public void Review_page_posts_to_approve_and_decline()
    {
        var html = SupportAccessMail.RenderReviewPage(Ctx("tr"), "https://crm/x/approve", "https://crm/x/decline");
        html.Should().Contain("method=\"post\" action=\"https://crm/x/approve\"").And.Contain("method=\"post\" action=\"https://crm/x/decline\"").And.Contain("İzin veriyorum (2 saat)").And.Contain("Reddet");
    }
}
