using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Mail;
using IonCrm.Application.Features.Marketing;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Intro e-mail sender. Materials ship inside the API image from <c>docs/marketing</c> (csproj copies
/// the folder to <c>marketing/</c> next to the binaries): every <c>*.html</c> is a template (Turkish first),
/// every <c>*.pdf</c> is attached. Sender: the shared company mailbox when configured
/// (<see cref="SmtpAccountResolver"/>, prefix <c>Marketing:Smtp</c>; in practice the support mailbox from
/// <c>EmailCollector:Imap:*</c>) — From carries the user's name, Reply-To and the BCC copy go to the user's own
/// address so the conversation still lands with the person who follows up. Without a shared mailbox the user's
/// own Gmail (address + app password stored encrypted on the user row) is used as before.
/// </summary>
public sealed class MarketingMailService : IMarketingMailService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(40);

    private readonly ApplicationDbContext _db;
    private readonly ITokenProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MarketingMailService> _logger;

    public MarketingMailService(ApplicationDbContext db, ITokenProtector protector, IConfiguration configuration, ILogger<MarketingMailService> logger)
    {
        _db = db; _protector = protector; _configuration = configuration; _logger = logger;
    }

    // ── Sender ───────────────────────────────────────────────────────────────

    private SmtpAccount? Shared => SmtpAccountResolver.Resolve(k => _configuration[k], "Marketing:Smtp");

    public string? SharedSenderAddress => Shared?.From;

    private static bool HasPersonalMailbox(Domain.Entities.User user) => !string.IsNullOrWhiteSpace(user.MailAddress) && !string.IsNullOrWhiteSpace(user.MailPasswordEncrypted);

    private static string SenderName(Domain.Entities.User user) => string.IsNullOrWhiteSpace(user.MailSenderName) ? user.FullName : user.MailSenderName.Trim();

    // ── Materials ────────────────────────────────────────────────────────────

    private string ResolveFolder()
    {
        var configured = _configuration["Marketing:Folder"];
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
        var published = Path.Combine(AppContext.BaseDirectory, "marketing");
        if (Directory.Exists(published)) return published;
        // Development: walk up to the repo's output/docs/marketing.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "marketing");
            if (Directory.Exists(candidate)) return candidate;
        }
        return published;
    }

    public MarketingMaterials GetMaterials()
    {
        var folder = ResolveFolder();
        var templates = new List<MarketingTemplateInfo>();
        var attachments = new List<MarketingAttachmentInfo>();
        if (Directory.Exists(folder))
        {
            foreach (var f in Directory.GetFiles(folder, "*.html").OrderBy(f => f))
            {
                var name = Path.GetFileName(f);
                var key = Path.GetFileNameWithoutExtension(f);
                var lang = key.Contains(".tr", StringComparison.OrdinalIgnoreCase) ? "tr" : key.Contains(".en-gb", StringComparison.OrdinalIgnoreCase) ? "en-GB" : key.Contains(".en", StringComparison.OrdinalIgnoreCase) ? "en" : null;
                var html = File.ReadAllText(f);
                if (lang is null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(html, "<html[^>]*lang=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success) lang = m.Groups[1].Value;
                }
                templates.Add(new MarketingTemplateInfo(key, name, IntroMailTemplate.ExtractSubjects(html), lang));
            }
            // Müşteriler Türk firmaları: Türkçe şablon varsayılan (listede ilk), diğerleri seçilebilir kalır.
            templates = templates.OrderByDescending(t => string.Equals(t.Language, "tr", StringComparison.OrdinalIgnoreCase)).ThenBy(t => t.FileName).ToList();
            foreach (var f in Directory.GetFiles(folder, "*.pdf").OrderBy(f => f))
                attachments.Add(new MarketingAttachmentInfo(Path.GetFileName(f), new FileInfo(f).Length));
        }
        return new MarketingMaterials(templates, attachments, folder);
    }

    // ── Sending ──────────────────────────────────────────────────────────────

    public async Task<MailSendResult> SendIntroAsync(Guid userId, Guid customerId, string? templateKey, int subjectIndex, string? note, string? toOverride, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null) return new MailSendResult(false, "Kullanıcı bulunamadı.");
        var shared = Shared;
        if (shared is null && !HasPersonalMailbox(user))
            return new MailSendResult(false, "Gönderici tanımlı değil: ortak posta kutusu yok (EmailCollector__Imap__* ya da Marketing__Smtp__*) ve Ayarlar → E-posta Gönderimi (Gmail) boş.");

        var customer = await _db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken);
        if (customer is null) return new MailSendResult(false, "Müşteri bulunamadı.");
        var to = (toOverride ?? customer.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(to) || !MailboxAddress.TryParse(to, out var toAddress))
            return new MailSendResult(false, "Müşterinin geçerli bir e-posta adresi yok; müşteri kartına e-posta girin ya da alıcıyı elle yazın.");

        var materials = GetMaterials();
        if (materials.Templates.Count == 0) return new MailSendResult(false, $"Tanıtım maili şablonu bulunamadı ({materials.Folder}/intro-email*.html).");
        var template = materials.Templates.FirstOrDefault(t => string.Equals(t.Key, templateKey, StringComparison.OrdinalIgnoreCase)) ?? materials.Templates[0];
        var html = await File.ReadAllTextAsync(Path.Combine(materials.Folder, template.FileName), cancellationToken);
        var senderName = SenderName(user);
        var fromAddress = shared?.From ?? user.MailAddress!;
        // Shared mailbox: replies (and the BCC copy) go to the user's own address — the signature shows that address too.
        var replyTo = shared is not null && MailboxAddress.TryParse(user.Email, out var own) && own.Address.Contains('@') ? own.Address : fromAddress;
        var rendered = IntroMailTemplate.Render(html, subjectIndex, customer.CompanyName, customer.ContactName, senderName, replyTo, note);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, fromAddress));
        message.To.Add(toAddress);
        message.ReplyTo.Add(new MailboxAddress(senderName, replyTo));
        if (user.MailBccSelf)
        {
            if (shared is null) message.Bcc.Add(new MailboxAddress(senderName, fromAddress));
            else if (!replyTo.Equals(fromAddress, StringComparison.OrdinalIgnoreCase)) message.Bcc.Add(new MailboxAddress(senderName, replyTo));
        }
        message.Subject = rendered.Subject;

        var builder = new BodyBuilder { HtmlBody = rendered.Html };
        var attached = new List<string>();
        foreach (var a in materials.Attachments)
        {
            await builder.Attachments.AddAsync(Path.Combine(materials.Folder, a.FileName), cancellationToken);
            attached.Add(a.FileName);
        }
        message.Body = builder.ToMessageBody();

        var result = await DeliverAsync(shared, user, message, cancellationToken);
        return result with { To = to, Subject = rendered.Subject, Attachments = attached };
    }

    public async Task<MailSendResult> SendTestAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null) return new MailSendResult(false, "Kullanıcı bulunamadı.");
        if (string.IsNullOrWhiteSpace(user.MailAddress) || string.IsNullOrWhiteSpace(user.MailPasswordEncrypted))
            return new MailSendResult(false, "Önce e-posta adresi ve uygulama şifresini kaydedin.");

        var senderName = SenderName(user);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, user.MailAddress));
        message.To.Add(new MailboxAddress(senderName, user.MailAddress));
        message.Subject = "ION CRM — Gmail test";
        message.Body = new BodyBuilder { HtmlBody = $"<p>Bu bir test iletisidir. ION CRM, <b>{System.Net.WebUtility.HtmlEncode(user.MailAddress)}</b> adresinden tanıtım maili gönderebilir.</p><p>{DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC</p>" }.ToMessageBody();
        var result = await DeliverAsync(null, user, message, cancellationToken);
        return result with { To = user.MailAddress, Subject = message.Subject };
    }

    /// <summary>Shared mailbox when given, else the user's own Gmail. Never throws for delivery problems — the result carries the Turkish reason.</summary>
    private async Task<MailSendResult> DeliverAsync(SmtpAccount? shared, Domain.Entities.User user, MimeMessage message, CancellationToken cancellationToken)
    {
        var from = message.From.Mailboxes.First().Address;
        try
        {
            if (shared is not null) await SmtpDelivery.SendAsync(shared, message, SendTimeout, cancellationToken);
            else await GmailSmtp.SendAsync(user.MailAddress!, _protector.Unprotect(user.MailPasswordEncrypted!), message, SendTimeout, cancellationToken);
            _logger.LogInformation("Pazarlama e-postası gönderildi: {From} → {To} ({Subject}) [{Source}]", from, string.Join(", ", message.To.Mailboxes.Select(m => m.Address)), message.Subject, shared?.Source ?? "Gmail");
            return new MailSendResult(true, "Gönderildi.", From: from);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pazarlama e-postası gönderilemedi: {From}", from);
            return new MailSendResult(false, shared is not null ? SmtpDelivery.Explain(ex, shared) : GmailSmtp.Explain(ex), From: from);
        }
    }
}
