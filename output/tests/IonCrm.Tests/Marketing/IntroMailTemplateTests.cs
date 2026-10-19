using FluentAssertions;
using IonCrm.Application.Features.Marketing;

namespace IonCrm.Tests.Marketing;

public class IntroMailTemplateTests
{
    private const string Template = """
        <!-- 1: One system from maintenance to payment — LiftDesk -->
        <!-- 2: Stop typing site work twice -->
        <!doctype html><html lang="en-GB"><head><title>LiftDesk — management software</title></head>
        <body><div style="x">Dear [Name],</div><div>Body for {{CompanyName}}.</div>
        <div>Kind regards,<br/><span>[Ad Soyad]</span></div></body></html>
        """;

    [Fact]
    public void Subjects_come_from_numbered_comments_then_title()
    {
        IntroMailTemplate.ExtractSubjects(Template).Should().Equal("One system from maintenance to payment — LiftDesk", "Stop typing site work twice");
        IntroMailTemplate.ExtractSubjects("<html><head><title>Only Title</title></head></html>").Should().Equal("Only Title");
        IntroMailTemplate.ExtractSubjects("<html></html>").Should().BeEmpty();
    }

    [Fact]
    public void Render_fills_placeholders_picks_subject_and_inserts_note_after_greeting()
    {
        var r = IntroMailTemplate.Render(Template, 1, "Yıldız Asansör", "Ahmet <Yılmaz>", "Ömer Çakmakcı", "omer@gmail.com", "Dün telefonda konuştuğumuz gibi.\nKatalog ekte.");

        r.Subject.Should().Be("Stop typing site work twice");
        r.Html.Should().Contain("Dear Ahmet &lt;Yılmaz&gt;,").And.Contain("Body for Yıldız Asansör.").And.Contain("<span>Ömer Çakmakcı</span>");
        r.Html.Should().NotContain("<!-- 1:").And.NotContain("[Name]").And.NotContain("[Ad Soyad]");
        var greeting = r.Html.IndexOf("Dear Ahmet", StringComparison.Ordinal);
        var note = r.Html.IndexOf("Dün telefonda", StringComparison.Ordinal);
        var body = r.Html.IndexOf("Body for", StringComparison.Ordinal);
        greeting.Should().BeLessThan(note);
        note.Should().BeLessThan(body, "note sits right after the greeting, before the body");
        r.Html.Should().Contain("Katalog ekte.").And.Contain("<br/>");
    }

    private const string TurkishTemplate = """
        <!-- 1: Asansör bakımından tahsilata kadar tek sistem — LiftDesk -->
        <html lang="tr"><body><div style="x">Sayın [Ad Soyad],</div><div>Gövde.</div>
        <div>Saygılarımla,<br/><span>[Ad Soyad]</span></div></body></html>
        """;

    [Fact]
    public void Turkish_template_greeting_is_recipient_and_signature_is_sender()
    {
        var withContact = IntroMailTemplate.Render(TurkishTemplate, 0, "Yıldız Asansör", "Ahmet Yılmaz", "Ömer Çakmakcı", "o@gmail.com", null);
        withContact.Html.Should().Contain("Sayın Ahmet Yılmaz,").And.Contain("<span>Ömer Çakmakcı</span>").And.NotContain("[Ad Soyad]");
        withContact.Subject.Should().Be("Asansör bakımından tahsilata kadar tek sistem — LiftDesk");

        var noContact = IntroMailTemplate.Render(TurkishTemplate, 0, "Yıldız Asansör", null, "Ömer Çakmakcı", "o@gmail.com", "Not.");
        noContact.Html.Should().Contain("Sayın Yıldız Asansör Yetkilisi,");
        noContact.Html.IndexOf("Not.", StringComparison.Ordinal).Should().BeGreaterThan(noContact.Html.IndexOf("Yetkilisi,", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_without_contact_name_uses_company_and_clamps_subject_index()
    {
        var r = IntroMailTemplate.Render(Template, 99, "Kartal Lift", null, "Ömer", "o@gmail.com", null);
        r.Subject.Should().Be("Stop typing site work twice");
        r.Html.Should().Contain("Dear Kartal Lift,");
    }
}
