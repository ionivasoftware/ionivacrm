using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Marketing;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Intro e-mail sender. Materials ship inside the API image from <c>docs/marketing</c> (csproj copies
/// the folder to <c>marketing/</c> next to the binaries): every <c>*.html</c> is a template (Turkish first),
/// every <c>*.pdf</c> is attached. The message goes out through the USER's own Gmail (address + app
/// password stored encrypted on the user row), so replies land in their inbox and the BCC copy keeps
/// the thread in their Sent/Inbox for follow-up. Gmail SMTP: smtp.gmail.com:587 STARTTLS.
/// </summary>
public sealed class MarketingMailService : IMarketingMailService
{
    private const string GmailHost = "smtp.gmail.com";
    private const int GmailPort = 587;
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(40);

    private readonly ApplicationDbContext _db;
    private readonly ITokenProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MarketingMailService> _logger;

    public MarketingMailService(ApplicationDbContext db, ITokenProtector protector, IConfiguration configuration, ILogger<MarketingMailService> logger)
    {
        _db = db; _protector = protector; _configuration = configuration; _logger = logger;
    }

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
        if (string.IsNullOrWhiteSpace(user.MailAddress) || string.IsNullOrWhiteSpace(user.MailPasswordEncrypted))
            return new MailSendResult(false, "Gönderici e-posta ayarları eksik — Ayarlar → E-posta Gönderimi (Gmail) bölümünü doldurun.");

        var customer = await _db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken);
        if (customer is null) return new MailSendResult(false, "Müşteri bulunamadı.");
        var to = (toOverride ?? customer.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(to) || !MailboxAddress.TryParse(to, out var toAddress))
            return new MailSendResult(false, "Müşterinin geçerli bir e-posta adresi yok; müşteri kartına e-posta girin ya da alıcıyı elle yazın.");

        var materials = GetMaterials();
        if (materials.Templates.Count == 0) return new MailSendResult(false, $"Tanıtım maili şablonu bulunamadı ({materials.Folder}/intro-email*.html).");
        var template = materials.Templates.FirstOrDefault(t => string.Equals(t.Key, templateKey, StringComparison.OrdinalIgnoreCase)) ?? materials.Templates[0];
        var html = await File.ReadAllTextAsync(Path.Combine(materials.Folder, template.FileName), cancellationToken);
        var senderName = string.IsNullOrWhiteSpace(user.MailSenderName) ? $"{user.FirstName} {user.LastName}".Trim() : user.MailSenderName.Trim();
        var rendered = IntroMailTemplate.Render(html, subjectIndex, customer.CompanyName, customer.ContactName, senderName, user.MailAddress, note);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, user.MailAddress));
        message.To.Add(toAddress);
        if (user.MailBccSelf) message.Bcc.Add(new MailboxAddress(senderName, user.MailAddress));
        message.ReplyTo.Add(new MailboxAddress(senderName, user.MailAddress));
        message.Subject = rendered.Subject;

        var builder = new BodyBuilder { HtmlBody = rendered.Html };
        var attached = new List<string>();
        foreach (var a in materials.Attachments)
        {
            await builder.Attachments.AddAsync(Path.Combine(materials.Folder, a.FileName), cancellationToken);
            attached.Add(a.FileName);
        }
        message.Body = builder.ToMessageBody();

        var result = await SendViaGmailAsync(user.MailAddress, _protector.Unprotect(user.MailPasswordEncrypted), message, cancellationToken);
        return result with { To = to, Subject = rendered.Subject, Attachments = attached };
    }

    public async Task<MailSendResult> SendTestAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null) return new MailSendResult(false, "Kullanıcı bulunamadı.");
        if (string.IsNullOrWhiteSpace(user.MailAddress) || string.IsNullOrWhiteSpace(user.MailPasswordEncrypted))
            return new MailSendResult(false, "Önce e-posta adresi ve uygulama şifresini kaydedin.");

        var senderName = string.IsNullOrWhiteSpace(user.MailSenderName) ? $"{user.FirstName} {user.LastName}".Trim() : user.MailSenderName.Trim();
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, user.MailAddress));
        message.To.Add(new MailboxAddress(senderName, user.MailAddress));
        message.Subject = "ION CRM — Gmail test";
        message.Body = new BodyBuilder { HtmlBody = $"<p>Bu bir test iletisidir. ION CRM, <b>{System.Net.WebUtility.HtmlEncode(user.MailAddress)}</b> adresinden tanıtım maili gönderebilir.</p><p>{DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC</p>" }.ToMessageBody();
        var result = await SendViaGmailAsync(user.MailAddress, _protector.Unprotect(user.MailPasswordEncrypted), message, cancellationToken);
        return result with { To = user.MailAddress, Subject = message.Subject };
    }

    private async Task<MailSendResult> SendViaGmailAsync(string address, string appPassword, MimeMessage message, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SendTimeout);
        try
        {
            using var client = new SmtpClient { Timeout = (int)SendTimeout.TotalMilliseconds };
            await client.ConnectAsync(GmailHost, GmailPort, SecureSocketOptions.StartTls, timeoutCts.Token);
            await client.AuthenticateAsync(address, appPassword, timeoutCts.Token);
            await client.SendAsync(message, timeoutCts.Token);
            await client.DisconnectAsync(true, timeoutCts.Token);
            _logger.LogInformation("Pazarlama e-postası gönderildi: {From} → {To} ({Subject})", address, string.Join(", ", message.To.Mailboxes.Select(m => m.Address)), message.Subject);
            return new MailSendResult(true, "Gönderildi.");
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning(ex, "Gmail kimlik doğrulaması reddedildi: {Address}", address);
            return new MailSendResult(false, "Gmail kullanıcı adı/şifreyi kabul etmedi. Google hesabında 2 Adımlı Doğrulama açık olmalı ve normal şifre değil 16 haneli Uygulama Şifresi kullanılmalı (Google Hesabı → Güvenlik → Uygulama şifreleri).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MailSendResult(false, "Gmail SMTP zaman aşımına uğradı (40 sn). Tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pazarlama e-postası gönderilemedi: {Address}", address);
            return new MailSendResult(false, $"Gönderilemedi: {ex.Message}");
        }
    }
}
