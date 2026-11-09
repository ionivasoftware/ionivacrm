using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Mail;
using IonCrm.Application.Features.Marketing;
using IonCrm.Application.Features.Outreach;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Global outreach: intro + timed follow-ups to approved non-TR leads, follow-ups threaded under the intro.
/// Sender: the shared company mailbox when configured (<see cref="SmtpAccountResolver"/>, prefix <c>Outreach:Smtp</c>;
/// in practice the support mailbox from <c>EmailCollector:Imap:*</c>) — the mail carries the approving user's name,
/// replies go to that mailbox and its IMAP side is checked before every follow-up for a reply or a bounce. Without a
/// shared mailbox the approving user's own Gmail is used as before. Materials live in <c>marketing/global</c>
/// (sequence.json + templates + optional PDFs). Daily cap (per mailbox when shared, else per sender) and a weekday
/// send window keep the account out of spam trouble.
/// Config: Outreach:DailyCapPerSender (40) · Outreach:MinGapSeconds (60) · Outreach:MaxPerCycle (25) · Outreach:Enabled.
/// </summary>
public sealed class OutreachService : IOutreachService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan ImapTimeout = TimeSpan.FromSeconds(40);

    private readonly ApplicationDbContext _db;
    private readonly IGlobalLeadRepository _leads;
    private readonly ITokenProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly IOutreachState _state;
    private readonly ILogger<OutreachService> _logger;

    public OutreachService(ApplicationDbContext db, IGlobalLeadRepository leads, ITokenProtector protector, IConfiguration configuration, IOutreachState state, ILogger<OutreachService> logger)
    {
        _db = db; _leads = leads; _protector = protector; _configuration = configuration; _state = state; _logger = logger;
    }

    // ── Sender ───────────────────────────────────────────────────────────────

    private SmtpAccount? Shared => SmtpAccountResolver.Resolve(k => _configuration[k], "Outreach:Smtp");

    public OutreachSender? SharedSender => Shared is { } s ? new OutreachSender(s.From, s.Source, s.HasImap) : null;

    private static bool HasPersonalMailbox(User user) => !string.IsNullOrWhiteSpace(user.MailAddress) && !string.IsNullOrWhiteSpace(user.MailPasswordEncrypted);

    private static string SenderName(User user) => string.IsNullOrWhiteSpace(user.MailSenderName) ? user.FullName : user.MailSenderName.Trim();

    // ── Materials ────────────────────────────────────────────────────────────

    private string Folder
    {
        get
        {
            var configured = _configuration["Outreach:Folder"];
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
            var published = Path.Combine(AppContext.BaseDirectory, "marketing", "global");
            if (Directory.Exists(published)) return published;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "marketing", "global");
                if (Directory.Exists(candidate)) return candidate;
            }
            return published;
        }
    }

    public SequenceDefinition GetSequence()
    {
        var path = Path.Combine(Folder, "sequence.json");
        try { return File.Exists(path) ? OutreachSequence.Parse(File.ReadAllText(path)) : SequenceDefinition.Default; }
        catch (Exception ex) { _logger.LogWarning(ex, "sequence.json okunamadı, varsayılan dizi"); return SequenceDefinition.Default; }
    }

    public SequenceDefinition GetSequenceFor(string? country)
    {
        var folder = Folder;
        return OutreachPolicy.ForCountry(GetSequence(), country, name => File.Exists(Path.Combine(folder, name)));
    }

    public (List<(string Template, int DelayDays, bool Attachments, List<string> Subjects, bool Exists)> Steps, List<string> Attachments, string Folder) GetMaterials()
    {
        var folder = Folder;
        var def = GetSequence();
        var steps = def.Steps.Select(s =>
        {
            var p = Path.Combine(folder, s.Template);
            var exists = File.Exists(p);
            return (s.Template, s.DelayDays, s.Attachments, exists ? IntroMailTemplate.ExtractSubjects(File.ReadAllText(p)) : new List<string>(), exists);
        }).ToList();
        var attachments = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.pdf").Select(Path.GetFileName).Where(f => f is not null).Select(f => f!).OrderBy(f => f).ToList() : new List<string>();
        return (steps, attachments, folder);
    }

    // ── Sending ──────────────────────────────────────────────────────────────

    public async Task<string?> SendNextStepAsync(GlobalLead lead, CancellationToken cancellationToken = default)
    {
        // Ülkeye göre dizi: Alman şablonu varsa o, saat dilimi alıcının (Europe/Berlin, Europe/Malta…).
        var def = GetSequenceFor(lead.Country);
        var step = lead.OutreachStep;
        if (step >= def.Steps.Count) { lead.OutreachStatus = OutreachStatus.Completed; lead.NextSendAt = null; await _leads.UpdateAsync(lead, cancellationToken); return null; }
        if (string.IsNullOrWhiteSpace(lead.Email) || !MailboxAddress.TryParse(lead.Email, out var to)) return await FailAsync(lead, "Lead'in geçerli e-posta adresi yok.");
        if (lead.SenderUserId is null) return await FailAsync(lead, "Gönderici kullanıcı atanmamış.");

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == lead.SenderUserId && !u.IsDeleted, cancellationToken);
        if (user is null) return await FailAsync(lead, "Gönderici kullanıcı bulunamadı.");
        var shared = Shared;
        if (shared is null && !HasPersonalMailbox(user))
            return await FailAsync(lead, "Gönderici yok: ortak posta kutusu tanımlı değil (EmailCollector__Imap__* ya da Outreach__Smtp__*) ve kullanıcının Gmail ayarı eksik (Ayarlar → E-posta Gönderimi).");
        var fromAddress = shared?.From ?? user.MailAddress!;

        var stepDef = def.Steps[step];
        var path = Path.Combine(Folder, stepDef.Template);
        if (!File.Exists(path)) return await FailAsync(lead, $"Şablon bulunamadı: {stepDef.Template}");
        var html = await File.ReadAllTextAsync(path, cancellationToken);
        var senderName = SenderName(user);
        var rendered = IntroMailTemplate.Render(html, 0, lead.CompanyName, lead.ContactName, senderName, fromAddress, null);

        if (string.IsNullOrWhiteSpace(lead.UnsubscribeToken)) lead.UnsubscribeToken = OutreachSequence.NewUnsubscribeToken();
        var baseUrl = SocialPostImageLinks.ResolvePublicApiBaseUrl(k => _configuration[k]) ?? "https://ion-crm-api-production.up.railway.app";
        var bodyHtml = OutreachSequence.InjectUnsubscribe(OutreachPolicy.ApplySitePlaceholders(rendered.Html, lead.Country), $"{baseUrl}/api/v1/outreach/unsubscribe/{lead.UnsubscribeToken}");

        // Follow-ups thread under the intro and carry "Re:" when the template brings no subject of its own.
        var intro = step > 0 ? (await _leads.GetEmailsAsync(lead.Id, cancellationToken)).FirstOrDefault(e => e.Step == 0 && e.Status == OutreachEmailStatus.Sent) : null;
        var subject = IntroMailTemplate.ExtractSubjects(html).Count > 0 ? rendered.Subject
            : intro is not null ? (intro.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? intro.Subject : $"Re: {intro.Subject}") : rendered.Subject;

        // Replies must land where the inbox check looks: the shared mailbox itself, or the user's Gmail.
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, fromAddress));
        message.To.Add(to);
        if (shared is null && user.MailBccSelf) message.Bcc.Add(new MailboxAddress(senderName, fromAddress));
        message.ReplyTo.Add(new MailboxAddress(senderName, fromAddress));
        message.Subject = subject;
        message.MessageId = MimeUtils.GenerateMessageId(fromAddress.Split('@').Last());
        if (step > 0 && !string.IsNullOrWhiteSpace(lead.ThreadMessageId))
        {
            message.InReplyTo = lead.ThreadMessageId;
            message.References.Add(lead.ThreadMessageId);
        }
        message.Headers.Add("List-Unsubscribe", $"<{baseUrl}/api/v1/outreach/unsubscribe/{lead.UnsubscribeToken}>");

        var builder = new BodyBuilder { HtmlBody = bodyHtml };
        if (stepDef.Attachments && Directory.Exists(Folder))
            foreach (var pdf in Directory.GetFiles(Folder, "*.pdf").OrderBy(f => f))
                await builder.Attachments.AddAsync(pdf, cancellationToken);
        message.Body = builder.ToMessageBody();

        var log = new GlobalLeadEmail { LeadId = lead.Id, Step = step, TemplateKey = stepDef.Template, Subject = subject, To = lead.Email!, SenderUserId = user.Id };
        try
        {
            if (shared is not null) await SmtpDelivery.SendAsync(shared, message, SendTimeout, cancellationToken);
            else await GmailSmtp.SendAsync(user.MailAddress!, _protector.Unprotect(user.MailPasswordEncrypted!), message, SendTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var reason = shared is not null ? SmtpDelivery.Explain(ex, shared) : GmailSmtp.Explain(ex);
            log.Status = OutreachEmailStatus.Failed; log.Error = reason; log.SentAt = DateTime.UtcNow;
            await _leads.AddEmailAsync(log, cancellationToken);
            _logger.LogWarning(ex, "Outreach gönderimi başarısız: lead {LeadId} adım {Step}", lead.Id, step);
            return await FailAsync(lead, reason, retryLater: ex is not AuthenticationException);
        }

        log.MessageId = message.MessageId; log.SentAt = DateTime.UtcNow;
        await _leads.AddEmailAsync(log, cancellationToken);

        lead.SentCount++; lead.LastSentAt = log.SentAt; lead.LastError = null;
        if (step == 0) lead.ThreadMessageId = message.MessageId;
        lead.OutreachStep = step + 1;
        if (lead.OutreachStep >= def.Steps.Count) { lead.OutreachStatus = OutreachStatus.Completed; lead.NextSendAt = null; }
        else { lead.OutreachStatus = OutreachStatus.Active; lead.NextSendAt = OutreachSequence.ScheduleStep(lead.OutreachStep, log.SentAt, def); }
        await _leads.UpdateAsync(lead, cancellationToken);
        _logger.LogInformation("Outreach gönderildi: {Company} ({Country}) adım {Step}/{Total} ← {Sender} ({Source})", lead.CompanyName, lead.Country, step + 1, def.Steps.Count, fromAddress, shared?.Source ?? "Gmail");
        return null;

        async Task<string> FailAsync(GlobalLead l, string reason, bool retryLater = false)
        {
            l.LastError = reason;
            if (retryLater) { l.NextSendAt = DateTime.UtcNow.AddHours(2); }
            else { l.OutreachStatus = OutreachStatus.Failed; l.NextSendAt = null; }
            await _leads.UpdateAsync(l, cancellationToken);
            return reason;
        }
    }

    // ── Inbox check (IMAP) ───────────────────────────────────────────────────

    public async Task<InboxCheck> CheckInboxAsync(GlobalLead lead, CancellationToken cancellationToken = default)
    {
        if (lead.SenderUserId is null || string.IsNullOrWhiteSpace(lead.Email) || lead.LastSentAt is null) return new(false, false, null);

        // Which inbox received the replies: the shared mailbox (IMAP side of the same account) or the sender's Gmail.
        string imapHost, imapUser, imapPassword; int imapPort; var checkRevocation = true;
        if (Shared is { } shared)
        {
            if (!shared.HasImap) return new(false, false, null);
            imapHost = shared.ImapHost!; imapPort = shared.ImapPort; imapUser = shared.Username!; imapPassword = shared.Password ?? string.Empty; checkRevocation = shared.CheckCertificateRevocation;
        }
        else
        {
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == lead.SenderUserId && !u.IsDeleted, cancellationToken);
            if (user is null || !HasPersonalMailbox(user)) return new(false, false, null);
            imapHost = GmailSmtp.ImapHost; imapPort = GmailSmtp.ImapPort; imapUser = user.MailAddress!; imapPassword = _protector.Unprotect(user.MailPasswordEncrypted!);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ImapTimeout);
        try
        {
            using var imap = new ImapClient { Timeout = (int)ImapTimeout.TotalMilliseconds, CheckCertificateRevocation = checkRevocation };
            await imap.ConnectAsync(imapHost, imapPort, SecureSocketOptions.SslOnConnect, timeoutCts.Token);
            await imap.AuthenticateAsync(imapUser, imapPassword, timeoutCts.Token);
            var inbox = imap.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, timeoutCts.Token);
            var since = lead.LastSentAt.Value.AddDays(-1);

            // Reply: anything from the lead's address (or its domain) after the last send.
            var fromQuery = SearchQuery.FromContains(lead.Email).And(SearchQuery.DeliveredAfter(since));
            var replies = await inbox.SearchAsync(fromQuery, timeoutCts.Token);
            if (replies.Count > 0)
            {
                var s = await inbox.FetchAsync(new[] { replies[^1] }, MessageSummaryItems.Envelope, timeoutCts.Token);
                return new(true, false, s.FirstOrDefault()?.Envelope?.Subject);
            }

            // Bounce: mailer-daemon messages mentioning the address.
            var bounceQuery = SearchQuery.FromContains("mailer-daemon").Or(SearchQuery.FromContains("postmaster")).And(SearchQuery.DeliveredAfter(since));
            var bounces = await inbox.SearchAsync(bounceQuery, timeoutCts.Token);
            foreach (var uid in bounces.TakeLast(10))
            {
                var msg = await inbox.GetMessageAsync(uid, timeoutCts.Token);
                var text = (msg.TextBody ?? string.Empty) + (msg.HtmlBody ?? string.Empty);
                if (text.Contains(lead.Email, StringComparison.OrdinalIgnoreCase)) return new(false, true, msg.Subject);
            }
            await imap.DisconnectAsync(true, timeoutCts.Token);
            return new(false, false, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Outreach IMAP kontrolü yapılamadı ({Mailbox})", imapUser);
            return new(false, false, $"IMAP: {ex.Message}");
        }
    }

    // ── Scheduler cycle ──────────────────────────────────────────────────────

    public async Task<OutreachRunSummary> RunDueAsync(CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow; var notes = new List<string>();
        int due = 0, sent = 0, replied = 0, bounced = 0, failed = 0, deferred = 0;
        try
        {
            if (!_configuration.GetValue("Outreach:Enabled", true)) { notes.Add("Outreach:Enabled=false"); return Finish(); }
            var def = GetSequence();
            var cap = Math.Max(1, _configuration.GetValue("Outreach:DailyCapPerSender", 40));
            var gap = TimeSpan.FromSeconds(Math.Clamp(_configuration.GetValue("Outreach:MinGapSeconds", 60), 5, 900));
            var maxPerCycle = Math.Clamp(_configuration.GetValue("Outreach:MaxPerCycle", 25), 1, 200);
            var now = DateTime.UtcNow;
            var list = await _leads.GetDueAsync(now, maxPerCycle, cancellationToken);
            due = list.Count;
            // Shared mailbox: one daily bucket for everyone (key Guid.Empty); personal Gmail: one per sender.
            var sharedMailbox = Shared is not null;
            var sentBySender = new Dictionary<Guid, int>();
            var dayStart = now.Date;

            foreach (var lead in list)
            {
                if (cancellationToken.IsCancellationRequested) break;
                // Window re-check (manual approvals outside hours get a NextSendAt inside the window already; this guards config changes).
                var windowed = OutreachSequence.NextSendTime(now, def);
                if (windowed > now.AddMinutes(1)) { lead.NextSendAt = windowed; await _leads.UpdateAsync(lead, cancellationToken); deferred++; continue; }

                var bucket = sharedMailbox ? Guid.Empty : lead.SenderUserId;
                if (bucket is { } key)
                {
                    if (!sentBySender.TryGetValue(key, out var count)) { count = await _leads.CountSentSinceAsync(sharedMailbox ? null : key, dayStart, cancellationToken); sentBySender[key] = count; }
                    if (count >= cap) { lead.NextSendAt = OutreachSequence.NextSendTime(dayStart.AddDays(1), def); await _leads.UpdateAsync(lead, cancellationToken); deferred++; continue; }
                }

                if (lead.OutreachStep > 0)
                {
                    var check = await CheckInboxAsync(lead, cancellationToken);
                    if (check.Replied) { lead.OutreachStatus = OutreachStatus.Replied; lead.RepliedAt = DateTime.UtcNow; lead.NextSendAt = null; lead.Notes = AppendNote(lead.Notes, $"Yanıt geldi: {check.Detail}"); await _leads.UpdateAsync(lead, cancellationToken); replied++; continue; }
                    if (check.Bounced) { lead.OutreachStatus = OutreachStatus.Bounced; lead.BouncedAt = DateTime.UtcNow; lead.NextSendAt = null; lead.Notes = AppendNote(lead.Notes, $"Bounce: {check.Detail}"); await _leads.UpdateAsync(lead, cancellationToken); bounced++; continue; }
                }

                var error = await SendNextStepAsync(lead, cancellationToken);
                if (error is null) { sent++; if (bucket is { } b2) sentBySender[b2] = sentBySender.GetValueOrDefault(b2) + 1; }
                else { failed++; notes.Add($"{lead.CompanyName}: {error}"); }

                try { await Task.Delay(gap, cancellationToken); } catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "Outreach döngüsü hatası"); notes.Add(ex.Message); }
        return Finish();

        OutreachRunSummary Finish()
        {
            var summary = new OutreachRunSummary(started, DateTime.UtcNow, due, sent, replied, bounced, failed, deferred, notes);
            _state.LastRun = summary;
            if (due > 0) _logger.LogInformation("Outreach: due={Due} sent={Sent} replied={Replied} bounced={Bounced} failed={Failed} deferred={Deferred}", due, sent, replied, bounced, failed, deferred);
            return summary;
        }
    }

    private static string AppendNote(string? existing, string line)
    {
        var stamped = $"{DateTime.UtcNow:dd.MM.yyyy HH:mm} {line}";
        return string.IsNullOrWhiteSpace(existing) ? stamped : $"{existing}\n{stamped}";
    }
}

public sealed class OutreachState : IOutreachState
{
    public OutreachRunSummary? LastRun { get; set; }
}
