using System.Text.RegularExpressions;

namespace IonCrm.Application.Features.Marketing;

/// <summary>
/// Pure helpers for the marketing intro e-mail: subject lines are read from the leading HTML comments
/// of the template (<c>&lt;!-- 1: ... --&gt;</c>), placeholders are filled from the customer and the
/// sending user, and an optional personal note is inserted right after the greeting line.
/// Supported placeholders: <c>[Name]</c>, <c>[Ad Soyad]</c>, <c>{{CompanyName}}</c>, <c>{{ContactName}}</c>,
/// <c>{{SenderName}}</c>, <c>{{SenderEmail}}</c>, <c>[Note]</c>.
/// </summary>
public static partial class IntroMailTemplate
{
    public record Rendered(string Subject, string Html);

    /// <summary>Numbered subject options from the top-of-file comments; falls back to the &lt;title&gt;.</summary>
    public static List<string> ExtractSubjects(string html)
    {
        var subjects = new List<string>();
        foreach (Match m in SubjectComment().Matches(html))
        {
            var s = m.Groups[1].Value.Trim();
            if (s.Length > 0) subjects.Add(s);
        }
        if (subjects.Count == 0)
        {
            var t = TitleTag().Match(html);
            if (t.Success && t.Groups[1].Value.Trim().Length > 0) subjects.Add(t.Groups[1].Value.Trim());
        }
        return subjects;
    }

    public static Rendered Render(string html, int subjectIndex, string companyName, string? contactName, string senderName, string senderEmail, string? note)
    {
        var subjects = ExtractSubjects(html);
        var subject = subjects.Count == 0 ? "LiftDesk" : subjects[Math.Clamp(subjectIndex, 0, subjects.Count - 1)];

        var greetingName = string.IsNullOrWhiteSpace(contactName) ? companyName : contactName.Trim();
        var body = html
            .Replace("[Name]", Encode(greetingName))
            .Replace("[Ad Soyad]", Encode(senderName))
            .Replace("{{CompanyName}}", Encode(companyName))
            .Replace("{{ContactName}}", Encode(greetingName))
            .Replace("{{SenderName}}", Encode(senderName))
            .Replace("{{SenderEmail}}", Encode(senderEmail));

        var noteHtml = string.IsNullOrWhiteSpace(note) ? string.Empty : NoteParagraph(note.Trim());
        if (body.Contains("[Note]"))
            body = body.Replace("[Note]", noteHtml);
        else if (noteHtml.Length > 0)
        {
            // Insert after the greeting block ("Dear X," / "Sayın X,") — the first <div> that ends with a comma.
            var m = GreetingDiv().Match(body);
            body = m.Success ? body.Insert(m.Index + m.Length, noteHtml) : noteHtml + body;
        }

        // Strip the subject comments so they do not travel with the message.
        body = SubjectComment().Replace(body, string.Empty);
        return new Rendered(subject, body);
    }

    private static string NoteParagraph(string note) =>
        "<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#0F1F33;padding-top:12px;\">" +
        Encode(note).Replace("\n", "<br/>") + "</div>";

    /// <summary>Minimal HTML escaping — the message is UTF-8, so Turkish letters stay as they are (WebUtility would turn ö into &amp;#246;).</summary>
    private static string Encode(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [GeneratedRegex(@"<!--\s*\d+\s*:\s*(.*?)\s*-->", RegexOptions.Singleline)]
    private static partial Regex SubjectComment();

    [GeneratedRegex(@"<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<div[^>]*>\s*(Dear|Sayın|Merhaba|Hello)\b[^<]*,\s*</div>", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingDiv();
}
