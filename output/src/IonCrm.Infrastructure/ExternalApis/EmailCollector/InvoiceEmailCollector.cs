using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.VendorInvoices;
using IonCrm.Application.Features.VendorInvoices.EmailCollector;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace IonCrm.Infrastructure.ExternalApis.EmailCollector;

/// <summary>
/// IMAP implementation of <see cref="IInvoiceEmailCollector"/> (MailKit).
///
/// Config (under <c>EmailCollector</c>):
///   Imap:Host, Imap:Port (default 993), Imap:Username, Imap:Password, Imap:UseSsl (default true),
///   Mailbox (default INBOX), LookbackDays (default 45), MaxMessages (default 300),
///   Rules[] (see <see cref="VendorEmailRule"/>).
///
/// The mailbox is opened read-only — messages are never modified or marked seen. Matching is
/// idempotent via MarkReceived's (provider, period) upsert, so re-scanning the same e-mail is safe.
/// </summary>
public sealed class InvoiceEmailCollector : IInvoiceEmailCollector
{
    private readonly IVendorInvoiceService _invoices;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvoiceEmailCollector> _logger;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Initialises a new instance of <see cref="InvoiceEmailCollector"/>.</summary>
    public InvoiceEmailCollector(IVendorInvoiceService invoices, IConfiguration configuration, ILogger<InvoiceEmailCollector> logger)
    {
        _invoices = invoices;
        _configuration = configuration;
        _logger = logger;
    }

    private string? Host => _configuration["EmailCollector:Imap:Host"];
    private string? Username => _configuration["EmailCollector:Imap:Username"];
    private string? Password => _configuration["EmailCollector:Imap:Password"];

    /// <inheritdoc />
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    /// <inheritdoc />
    public async Task<Result<EmailCollectSummary>> CollectAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return Result<EmailCollectSummary>.Failure("E-posta toplayıcı yapılandırılmamış (IMAP host/kullanıcı/şifre eksik).");

        var rules = LoadRules();
        if (rules.Count == 0)
            return Result<EmailCollectSummary>.Failure("Hiç e-posta kuralı tanımlı değil (EmailCollector:Rules).");

        var port = _configuration.GetValue("EmailCollector:Imap:Port", 993);
        var useSsl = _configuration.GetValue("EmailCollector:Imap:UseSsl", true);
        var mailbox = _configuration["EmailCollector:Mailbox"];
        var lookbackDays = _configuration.GetValue("EmailCollector:LookbackDays", 45);
        var maxMessages = _configuration.GetValue("EmailCollector:MaxMessages", 300);

        var items = new List<EmailCollectItem>();
        var scanned = 0;
        var received = 0;

        try
        {
            using var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
            // Host/Username/Password are non-null here — guarded by the IsConfigured check above.
            await client.ConnectAsync(Host!, port,
                useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(Username!, Password!, cancellationToken);

            var folder = string.IsNullOrWhiteSpace(mailbox) || mailbox.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await client.GetFolderAsync(mailbox, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var since = DateTime.UtcNow.AddDays(-Math.Abs(lookbackDays));
            var uids = await folder.SearchAsync(SearchQuery.DeliveredAfter(since), cancellationToken);

            // Newest first, capped.
            foreach (var uid in uids.Reverse().Take(maxMessages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var message = await folder.GetMessageAsync(uid, cancellationToken);
                scanned++;

                var from = $"{message.From}";
                var subject = message.Subject ?? string.Empty;
                var body = message.TextBody ?? StripHtml(message.HtmlBody) ?? string.Empty;
                var emailDate = message.Date.UtcDateTime;

                var rule = rules.FirstOrDefault(r => Matches(r, from, subject, body));
                if (rule is null)
                {
                    // Dry-run: surface unmatched messages so their real From/Subject can be inspected
                    // (forwarded mail rewrites From — this reveals what to match on).
                    if (dryRun)
                        items.Add(new EmailCollectItem("(eşleşme yok)", 0, 0, null, null, null,
                            subject, emailDate, "unmatched", "from: " + Snippet(from, 160)));
                    continue;
                }

                // Google (and others) only put the amount in the attached PDF — include its text, and
                // keep the raw bytes so the PDF can be stored and viewed in the CRM.
                var pdfAttachment = GetFirstPdfAttachment(message);
                var pdfText = pdfAttachment is null ? string.Empty : ExtractPdfText(pdfAttachment.Value.Bytes);
                var haystack = subject + "\n" + body + "\n" + pdfText;
                decimal? amount = TryExtractAmount(rule.AmountRegex, haystack, out var parsedAmount) ? parsedAmount : null;
                var invoiceNo = Truncate(TryExtractGroup(rule.InvoiceNoRegex, haystack), 100);
                var pdfUrl = Truncate(TryExtractGroup(rule.PdfUrlRegex, body), 1000);

                // Dry-run diagnostic: surface a text snippet so the amount regex can be crafted/verified.
                string? diag = dryRun
                    ? "metin: " + Snippet(string.IsNullOrWhiteSpace(pdfText) ? body : pdfText, 900)
                    : null;
                var currency = string.IsNullOrWhiteSpace(rule.Currency) ? "USD" : rule.Currency;

                // Derive the period from the invoice's own date when available (forwarded mail loses the
                // original send date — the e-mail date is the forward date, which collapses everything to
                // one month). Fall back to the e-mail date only when no in-content date is found.
                var periodBase = emailDate;
                var dateStr = TryExtractGroup(rule.DateRegex, haystack);
                if (dateStr is not null && TryParseDate(dateStr, out var invoiceDate))
                    periodBase = invoiceDate;

                var period = periodBase.AddMonths(-rule.PeriodMonthOffset);
                var year = period.Year;
                var month = period.Month;

                if (amount is null)
                {
                    items.Add(new EmailCollectItem(rule.Provider, year, month, null, currency, invoiceNo, subject, emailDate,
                        "no-amount", diag ?? "Tutar regex eşleşmedi"));
                    continue;
                }

                if (dryRun)
                {
                    items.Add(new EmailCollectItem(rule.Provider, year, month, amount, currency, invoiceNo, subject, emailDate,
                        "preview", diag));
                    continue;
                }

                var res = await _invoices.MarkReceivedAsync(
                    new MarkReceivedRequest(rule.Provider, year, month, amount, currency, invoiceNo, pdfUrl),
                    cancellationToken);

                if (res.IsSuccess)
                {
                    received++;
                    // Store the PDF attachment so it can be viewed in the CRM.
                    if (pdfAttachment is not null && res.Value is not null)
                        await _invoices.SavePdfAsync(res.Value.Id, pdfAttachment.Value.FileName,
                            "application/pdf", pdfAttachment.Value.Bytes, cancellationToken);

                    items.Add(new EmailCollectItem(rule.Provider, year, month, amount, currency, invoiceNo, subject, emailDate,
                        "received", null));
                }
                else
                {
                    items.Add(new EmailCollectItem(rule.Provider, year, month, amount, currency, invoiceNo, subject, emailDate,
                        "failed", res.FirstError));
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InvoiceEmailCollector: IMAP scan failed.");
            return Result<EmailCollectSummary>.Failure($"IMAP tarama hatası: {ex.Message}");
        }

        var matched = items.Count;
        _logger.LogInformation("InvoiceEmailCollector: {Scanned} tarandı, {Matched} eşleşme, {Received} işlendi (dryRun={DryRun}).",
            scanned, matched, received, dryRun);
        return Result<EmailCollectSummary>.Success(new EmailCollectSummary(scanned, matched, received, items));
    }

    // ── Rules ───────────────────────────────────────────────────────────────

    private List<VendorEmailRule> LoadRules()
    {
        var rules = _configuration.GetSection("EmailCollector:Rules").Get<List<VendorEmailRule>>();
        return (rules ?? new List<VendorEmailRule>())
            .Where(r => !string.IsNullOrWhiteSpace(r.Provider))
            .ToList();
    }

    private static bool Matches(VendorEmailRule rule, string from, string subject, string body)
    {
        // Forwarded mail rewrites the From header to the forwarder, but the original sender survives
        // in the quoted body ("From: ...@vendor"), so match FromContains against From + body.
        if (!string.IsNullOrWhiteSpace(rule.FromContains)
            && (from + "\n" + body).IndexOf(rule.FromContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
        // Manual forwards prepend "Fwd:" but keep the original subject as a substring.
        if (!string.IsNullOrWhiteSpace(rule.SubjectContains)
            && subject.IndexOf(rule.SubjectContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (!string.IsNullOrWhiteSpace(rule.BodyContains)
            && body.IndexOf(rule.BodyContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
        // A rule needs at least one positive matcher to avoid matching everything.
        return !string.IsNullOrWhiteSpace(rule.FromContains)
            || !string.IsNullOrWhiteSpace(rule.SubjectContains)
            || !string.IsNullOrWhiteSpace(rule.BodyContains);
    }

    private static bool TryExtractAmount(string? pattern, string text, out decimal value)
    {
        value = 0m;
        var raw = TryExtractGroup(pattern, text);
        return raw is not null && TryParseMoney(raw, out value);
    }

    private static string? TryExtractGroup(string? pattern, string text)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrEmpty(text)) return null;
        try
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success) return null;
            return m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
        }
        catch (RegexParseException) { return null; }
    }

    /// <summary>Parses an invoice date string tolerating several common formats (English month names, ISO, etc.).</summary>
    private static bool TryParseDate(string raw, out DateTime date)
    {
        var s = raw.Trim();
        string[] formats =
        {
            "MMMM d, yyyy", "MMM d, yyyy", "MMMM d yyyy", "MMM d yyyy",
            "d MMMM yyyy", "d MMM yyyy", "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy",
        };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Parses a money string tolerating both "1,234.56" and "1.234,56" grouping conventions.</summary>
    private static bool TryParseMoney(string raw, out decimal value)
    {
        value = 0m;
        var s = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
        if (s.Length == 0) return false;

        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        char dec = lastDot > lastComma ? '.' : (lastComma > lastDot ? ',' : '\0');

        if (dec == '\0')
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

        var thousands = dec == '.' ? "," : ".";
        s = s.Replace(thousands, string.Empty).Replace(dec, '.');
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);

    /// <summary>Whitespace-collapsed leading snippet, for the dry-run diagnostic.</summary>
    private static string Snippet(string text, int max)
    {
        var collapsed = Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim();
        return collapsed.Length <= max ? collapsed : collapsed[..max];
    }

    /// <summary>Returns the bytes + file name of the first PDF attachment on the message, or null.</summary>
    private static (byte[] Bytes, string? FileName)? GetFirstPdfAttachment(MimeMessage message)
    {
        foreach (var entity in message.Attachments)
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

    /// <summary>Extracts text from PDF bytes (capped), for amount/date matching.</summary>
    private string ExtractPdfText(byte[] bytes)
    {
        try
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(bytes);
            foreach (var page in pdf.GetPages())
            {
                sb.Append(page.Text).Append('\n');
                if (sb.Length > 20000) break;
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "InvoiceEmailCollector: PDF parse failed.");
            return string.Empty;
        }
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var text = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ").Trim();
    }
}
