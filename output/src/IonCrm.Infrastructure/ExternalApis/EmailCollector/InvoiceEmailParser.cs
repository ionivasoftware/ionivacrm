using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

namespace IonCrm.Infrastructure.ExternalApis.EmailCollector;

/// <summary>
/// Flattens an invoice e-mail into the few strings the rules match on, seeing through forwards.
///
/// Two forward styles reach the accounting mailbox and they look very different on the wire:
///   • inline forward — attachments stay at the top level and the original headers are quoted in the body;
///   • forward AS ATTACHMENT — the whole original mail becomes a nested <c>message/rfc822</c> part, so the
///     vendor's From/Subject and the invoice PDF are one level down and invisible to
///     <see cref="MimeMessage.Attachments"/>, which does not descend into embedded messages.
///
/// The second style silently produced "no match" / "no amount" for forwarded Google Workspace invoices
/// (their amount exists only inside the attached PDF). Everything here therefore walks the whole MIME
/// tree, including embedded messages.
/// </summary>
public static class InvoiceEmailParser
{
    /// <summary>Everything the collector needs from one message, forwards already resolved.</summary>
    /// <param name="From">Outer sender plus any embedded message senders — matched against FromContains.</param>
    /// <param name="Subject">Outer subject, for display.</param>
    /// <param name="SubjectForMatch">Outer subject plus embedded subjects — matched against SubjectContains.</param>
    /// <param name="Body">Outer body plus embedded bodies (HTML stripped).</param>
    /// <param name="PdfBytes">First PDF found anywhere in the tree, or null.</param>
    /// <param name="PdfFileName">Its file name, when it has one.</param>
    /// <param name="Date">
    /// The original message's date when the mail was forwarded as an attachment, otherwise the
    /// message's own date. This is the invoice's real date, which is what the billing period derives from.
    /// </param>
    public sealed record ParsedEmail(
        string From,
        string Subject,
        string SubjectForMatch,
        string Body,
        byte[]? PdfBytes,
        string? PdfFileName,
        DateTime Date);

    /// <summary>Parses <paramref name="message"/>, resolving both forward styles.</summary>
    public static ParsedEmail Parse(MimeMessage message)
    {
        var nested = EmbeddedMessages(message).ToList();

        var from = new StringBuilder($"{message.From}");
        var subjectForMatch = new StringBuilder(message.Subject ?? string.Empty);
        var body = new StringBuilder(BodyTextOf(message));

        foreach (var inner in nested)
        {
            from.Append('\n').Append(inner.From);
            subjectForMatch.Append('\n').Append(inner.Subject);
            body.Append('\n').Append(BodyTextOf(inner));
        }

        // An attachment-forward carries the original send date; prefer it over the forward's date so
        // the billing period does not collapse onto the day the mail happened to be forwarded.
        var date = message.Date.UtcDateTime;
        var innerDate = nested.Select(m => m.Date).FirstOrDefault(d => d != default);
        if (innerDate != default) date = innerDate.UtcDateTime;

        var pdf = FirstPdf(message.Body);

        return new ParsedEmail(
            From: from.ToString(),
            Subject: message.Subject ?? string.Empty,
            SubjectForMatch: subjectForMatch.ToString(),
            Body: body.ToString(),
            PdfBytes: pdf?.Bytes,
            PdfFileName: pdf?.FileName,
            Date: date);
    }

    /// <summary>Every message embedded in the tree (forward-as-attachment), outermost first.</summary>
    private static IEnumerable<MimeMessage> EmbeddedMessages(MimeMessage message)
    {
        foreach (var entity in Flatten(message.Body, includeMessages: true))
        {
            if (entity is MessagePart { Message: not null } messagePart)
                yield return messagePart.Message;
        }
    }

    /// <summary>First PDF anywhere in the tree, including inside embedded messages.</summary>
    private static (byte[] Bytes, string? FileName)? FirstPdf(MimeEntity? root)
    {
        foreach (var entity in Flatten(root, includeMessages: false))
        {
            if (entity is not MimePart part || part.Content is null) continue;

            var fileName = part.FileName ?? string.Empty;
            var isPdf = part.ContentType.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            if (!isPdf) continue;

            using var ms = new MemoryStream();
            part.Content.DecodeTo(ms);
            return (ms.ToArray(), string.IsNullOrWhiteSpace(fileName) ? null : fileName);
        }
        return null;
    }

    /// <summary>
    /// Walks the MIME tree. <paramref name="includeMessages"/> yields the <see cref="MessagePart"/>
    /// wrappers themselves (to collect embedded messages); either way the walk descends INTO them, which
    /// is what <see cref="MimeMessage.Attachments"/> does not do.
    /// </summary>
    private static IEnumerable<MimeEntity> Flatten(MimeEntity? entity, bool includeMessages)
    {
        switch (entity)
        {
            case null:
                yield break;

            case Multipart multipart:
                foreach (var child in multipart)
                    foreach (var e in Flatten(child, includeMessages))
                        yield return e;
                break;

            case MessagePart messagePart:
                if (includeMessages) yield return messagePart;
                foreach (var e in Flatten(messagePart.Message?.Body, includeMessages))
                    yield return e;
                break;

            default:
                yield return entity;
                break;
        }
    }

    /// <summary>Plain-text body, falling back to the HTML part with tags stripped.</summary>
    private static string BodyTextOf(MimeMessage message)
        => message.TextBody ?? StripHtml(message.HtmlBody) ?? string.Empty;

    /// <summary>Reduces an HTML body to matchable text.</summary>
    public static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var text = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ").Trim();
    }
}
