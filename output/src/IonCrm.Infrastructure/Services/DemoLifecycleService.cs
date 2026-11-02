using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Features.Lifecycle;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Demo lifecycle e-mails for non-TR Liftdesk trial accounts, sent from the company support mailbox:
/// welcome (right after signup), check-in (mid-trial), trial-ended reminder (expired without purchase).
/// Rules live in <see cref="DemoLifecyclePlanner"/>; templates in <c>marketing/lifecycle/{lang}/</c>
/// (language from the customer's country, "en" fallback). Each send is logged in CustomerLifecycleMails
/// (one per customer+step) and on the customer's timeline (E-posta). SMTP account: <see cref="LifecycleSmtpResolver"/>
/// (<c>LifecycleMail:Smtp:*</c> → support mailbox from <c>EmailCollector:Imap:*</c> → <c>Notifications:Smtp:*</c>).
/// Config: LifecycleMail:Enabled, CheckInAfterDays (3),
/// TrialEndedAfterDays (1), MaxAgeDays (14), SendHourStart (8), SendHourEnd (20), ExcludeCountries ("TR"),
/// PortalUrl, SupportEmail, Folder.
/// </summary>
public sealed class DemoLifecycleService : IDemoLifecycleService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(40);

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IDemoLifecycleState _state;
    private readonly ILogger<DemoLifecycleService> _logger;

    public DemoLifecycleService(ApplicationDbContext db, IConfiguration configuration, IDemoLifecycleState state, ILogger<DemoLifecycleService> logger)
    {
        _db = db; _configuration = configuration; _state = state; _logger = logger;
    }

    public LifecycleOptions Options => new(
        Math.Clamp(_configuration.GetValue("LifecycleMail:CheckInAfterDays", 3), 1, 30),
        Math.Clamp(_configuration.GetValue("LifecycleMail:TrialEndedAfterDays", 1), 0, 30),
        Math.Clamp(_configuration.GetValue("LifecycleMail:MaxAgeDays", 14), 1, 90),
        Math.Clamp(_configuration.GetValue("LifecycleMail:SendHourStart", 8), 0, 23),
        Math.Clamp(_configuration.GetValue("LifecycleMail:SendHourEnd", 20), 1, 24),
        _configuration["LifecycleMail:ExcludeCountries"] is { Length: > 0 } ex ? ex : "TR");

    private bool Enabled => _configuration.GetValue("LifecycleMail:Enabled", true);

    // ── SMTP (support mailbox) ───────────────────────────────────────────────

    private const string SmtpMissing = "SMTP ayarı yok (LifecycleMail__Smtp__Host/Username/Password/From, ya da destek kutusu için EmailCollector__Imap__Host/Username/Password).";

    private LifecycleSmtp? Smtp => LifecycleSmtpResolver.Resolve(key => _configuration[key]);
    private string? FromAddress => Smtp?.From;
    private string FromName => _configuration["LifecycleMail:Smtp:FromName"] ?? _configuration["LifecycleMail:FromName"] ?? "LiftDesk";
    private bool SmtpConfigured => Smtp is not null;
    private string SupportEmail => _configuration["LifecycleMail:SupportEmail"] ?? FromAddress ?? "support@liftdesk.app";
    private string PortalUrl => (_configuration["LifecycleMail:PortalUrl"] ?? "https://portal.liftdesk.app").TrimEnd('/');

    // ── Templates ────────────────────────────────────────────────────────────

    private string Folder
    {
        get
        {
            var configured = _configuration["LifecycleMail:Folder"];
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
            var published = Path.Combine(AppContext.BaseDirectory, "marketing", "lifecycle");
            if (Directory.Exists(published)) return published;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "marketing", "lifecycle");
                if (Directory.Exists(candidate)) return candidate;
            }
            return published;
        }
    }

    private List<string> Languages => Directory.Exists(Folder)
        ? Directory.GetDirectories(Folder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).OrderBy(n => n).ToList()
        : new List<string>();

    private bool TemplateExists(string lang, LifecycleStep step) => File.Exists(Path.Combine(Folder, lang, LifecycleTemplate.FileFor(step)));

    private string ResolveLanguage(string? country, LifecycleStep step) =>
        DemoLifecyclePlanner.ResolveLanguage(country, l => TemplateExists(l, step));

    // ── Queries ──────────────────────────────────────────────────────────────

    private static LifecycleCustomer Project(Customer c) =>
        new(c.Id, c.Status, DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc), c.ExpirationDate, c.Country, c.Email, c.IsInternal, c.MarketingOptOut, c.LegacyId);

    private IQueryable<Customer> Scope(DateTime nowUtc)
    {
        var since = nowUtc.AddDays(-(Options.MaxAgeDays + Options.CheckInAfterDays + 1));
        return _db.Customers.IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.LegacyId != null && c.LegacyId.StartsWith("LIFT-")
                        && ((c.Status == CustomerStatus.Demo && c.CreatedAt >= since)
                            || (c.Status == CustomerStatus.Passive && c.ExpirationDate != null && c.ExpirationDate >= since)));
    }

    private async Task<Dictionary<Guid, HashSet<LifecycleStep>>> SentStepsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var set = ids.ToList();
        var rows = await _db.CustomerLifecycleMails.IgnoreQueryFilters().Where(m => !m.IsDeleted && set.Contains(m.CustomerId)).Select(m => new { m.CustomerId, m.Step }).ToListAsync(ct);
        return rows.GroupBy(r => r.CustomerId).ToDictionary(g => g.Key, g => g.Select(x => x.Step).ToHashSet());
    }

    public async Task<LifecycleStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _db.CustomerLifecycleMails.IgnoreQueryFilters().Where(m => !m.IsDeleted && m.Status == LifecycleMailStatus.Sent)
            .GroupBy(m => m.Step).Select(g => new { g.Key, C = g.Count() }).ToListAsync(cancellationToken);
        return new LifecycleStatus(Enabled, SmtpConfigured, FromAddress, Smtp?.Source, Folder, Languages, Options,
            Enum.GetValues<LifecycleStep>().ToDictionary(s => s.ToString(), s => counts.FirstOrDefault(x => x.Key == s)?.C ?? 0), _state.LastRun);
    }

    public async Task<List<LifecycleCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow; var o = Options;
        var customers = await Scope(now).OrderByDescending(c => c.CreatedAt).Take(300).ToListAsync(cancellationToken);
        var sent = await SentStepsAsync(customers.Select(c => c.Id), cancellationToken);
        return customers.Select(c =>
        {
            var lc = Project(c);
            var steps = sent.GetValueOrDefault(c.Id) ?? new HashSet<LifecycleStep>();
            var due = DemoLifecyclePlanner.NextStep(lc, steps, now, o);
            var reason = DemoLifecyclePlanner.IneligibleReason(lc, o);
            return new LifecycleCandidate(c.Id, c.CompanyName, c.Email, c.Country, ResolveLanguage(c.Country, due ?? LifecycleStep.Welcome), c.Status, lc.CreatedAt, c.ExpirationDate,
                due, reason, steps.OrderBy(s => s).ToList(), reason is null && DemoLifecyclePlanner.InSendWindow(now, c.Country, o));
        }).ToList();
    }

    public async Task<LifecycleTemplate.Rendered?> PreviewAsync(Guid customerId, LifecycleStep step, CancellationToken cancellationToken = default)
    {
        var c = await _db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == customerId && !x.IsDeleted, cancellationToken);
        if (c is null) return null;
        var lang = ResolveLanguage(c.Country, step);
        var path = Path.Combine(Folder, lang, LifecycleTemplate.FileFor(step));
        if (!File.Exists(path)) return null;
        return LifecycleTemplate.Render(await File.ReadAllTextAsync(path, cancellationToken), c.CompanyName, c.ContactName, c.ExpirationDate, c.Country, PortalUrl, SupportEmail, UnsubscribeUrl(c.Id), DateTime.UtcNow);
    }

    // ── Sending ──────────────────────────────────────────────────────────────

    private string UnsubscribeUrl(Guid customerId)
    {
        var baseUrl = SocialPostImageLinks.ResolvePublicApiBaseUrl(k => _configuration[k]) ?? "https://ion-crm-api-production.up.railway.app";
        var secret = _configuration["Social:TokenKey"] is { Length: > 0 } k ? k : _configuration["JwtSettings:Secret"] ?? "ion-crm";
        return $"{baseUrl}/api/v1/outreach/unsubscribe/customer/{customerId}/{DemoLifecyclePlanner.UnsubscribeToken(customerId, secret)}";
    }

    public async Task<string?> SendAsync(Guid customerId, LifecycleStep step, bool force, string triggeredBy, CancellationToken cancellationToken = default)
    {
        var c = await _db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == customerId && !x.IsDeleted, cancellationToken);
        if (c is null) return "Müşteri bulunamadı.";
        var already = await _db.CustomerLifecycleMails.IgnoreQueryFilters().AnyAsync(m => !m.IsDeleted && m.CustomerId == customerId && m.Step == step && m.Status == LifecycleMailStatus.Sent, cancellationToken);
        if (already) return "Bu adım bu müşteriye zaten gönderilmiş.";
        var lc = Project(c);
        var o = Options;
        if (!force)
        {
            if (DemoLifecyclePlanner.IneligibleReason(lc, o) is { } why) return $"Kapsam dışı: {why}.";
            var sentSteps = (await SentStepsAsync(new[] { c.Id }, cancellationToken)).GetValueOrDefault(c.Id) ?? new HashSet<LifecycleStep>();
            if (DemoLifecyclePlanner.NextStep(lc, sentSteps, DateTime.UtcNow, o) != step) return "Bu adım şu an sırada değil (zorla göndermek için force).";
        }
        if (string.IsNullOrWhiteSpace(c.Email) || !MailboxAddress.TryParse(c.Email, out var to)) return "Müşterinin geçerli e-posta adresi yok.";
        if (Smtp is not { } smtp) return SmtpMissing;

        var lang = ResolveLanguage(c.Country, step);
        var path = Path.Combine(Folder, lang, LifecycleTemplate.FileFor(step));
        if (!File.Exists(path)) return $"Şablon yok: lifecycle/{lang}/{LifecycleTemplate.FileFor(step)}";
        var rendered = LifecycleTemplate.Render(await File.ReadAllTextAsync(path, cancellationToken), c.CompanyName, c.ContactName, c.ExpirationDate, c.Country, PortalUrl, SupportEmail, UnsubscribeUrl(c.Id), DateTime.UtcNow);
        var message = BuildMessage(smtp, to, rendered, UnsubscribeUrl(c.Id));

        var log = new CustomerLifecycleMail { CustomerId = c.Id, Step = step, Language = lang, To = c.Email!, Subject = rendered.Subject, TriggeredBy = triggeredBy, SentAt = DateTime.UtcNow };
        try
        {
            await DeliverAsync(smtp, message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var reason = DeliveryError(ex);
            log.Status = LifecycleMailStatus.Failed; log.Error = reason;
            _db.CustomerLifecycleMails.Add(log);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(ex, "Lifecycle mail başarısız: {Customer} {Step}", c.CompanyName, step);
            return reason;
        }

        log.MessageId = message.MessageId;
        _db.CustomerLifecycleMails.Add(log);
        _db.ContactHistories.Add(new ContactHistory
        {
            CustomerId = c.Id, ProjectId = c.ProjectId, Type = ContactType.Email,
            Subject = $"Demo e-postası: {StepLabel(step)} ({lang})",
            Content = $"Konu: {rendered.Subject}\nAlıcı: {c.Email}\nGönderen: {FromAddress}\nTetik: {triggeredBy}",
            ContactedAt = DateTime.UtcNow, Purpose = AppointmentKind.Demo, Reached = true,
        });
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Lifecycle mail gönderildi: {Customer} ({Country}) {Step} [{Lang}] → {To}", c.CompanyName, c.Country, step, lang, c.Email);
        return null;
    }

    public async Task<LifecycleTestResult> SendTestAsync(string to, LifecycleStep step, string? language, string triggeredBy, CancellationToken cancellationToken = default)
    {
        var smtp = Smtp;
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
        LifecycleTestResult Fail(string error) => new(error, to, lang, null, smtp?.From, smtp?.Source);

        if (string.IsNullOrWhiteSpace(to) || !MailboxAddress.TryParse(to.Trim(), out var address) || !address.Address.Contains('@')) return Fail("Geçerli bir e-posta adresi girin.");
        if (!TemplateExists(lang, step)) return Fail($"Şablon yok: lifecycle/{lang}/{LifecycleTemplate.FileFor(step)}");
        if (smtp is null) return Fail(SmtpMissing);

        // Sample data: a believable company on the language's home market; trial end placed where the step expects it.
        var now = DateTime.UtcNow;
        var country = DemoLifecyclePlanner.SampleCountryFor(lang);
        DateTime trialEnd = step switch { LifecycleStep.Welcome => now.AddDays(7), LifecycleStep.CheckIn => now.AddDays(4), _ => now.AddDays(-1) };
        var unsubscribe = UnsubscribeUrl(Guid.Empty); // real-shaped link that resolves to "invalid link" — nothing to opt out
        var html = await File.ReadAllTextAsync(Path.Combine(Folder, lang, LifecycleTemplate.FileFor(step)), cancellationToken);
        var rendered = LifecycleTemplate.Render(html, "Apex Lifts Ltd", "Jane Carter", trialEnd, country, PortalUrl, SupportEmail, unsubscribe, now);
        var message = BuildMessage(smtp, address, rendered with { Subject = "[TEST] " + rendered.Subject }, unsubscribe);

        try
        {
            await DeliverAsync(smtp, message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lifecycle deneme e-postası başarısız: {Step} [{Lang}] → {To} ({Source})", step, lang, address.Address, smtp.Source);
            return Fail(DeliveryError(ex));
        }
        _logger.LogInformation("Lifecycle deneme e-postası gönderildi: {Step} [{Lang}] → {To} ({Source}, {By})", step, lang, address.Address, smtp.Source, triggeredBy);
        return new LifecycleTestResult(null, address.Address, lang, message.Subject, smtp.From, smtp.Source);
    }

    private MimeMessage BuildMessage(LifecycleSmtp smtp, MailboxAddress to, LifecycleTemplate.Rendered rendered, string unsubscribeUrl)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromName, smtp.From));
        message.To.Add(to);
        message.ReplyTo.Add(new MailboxAddress(FromName, SupportEmail));
        message.Subject = rendered.Subject;
        message.MessageId = MimeUtils.GenerateMessageId(smtp.From.Split('@').Last());
        message.Headers.Add("List-Unsubscribe", $"<{unsubscribeUrl}>");
        message.Body = new BodyBuilder { HtmlBody = rendered.Html }.ToMessageBody();
        return message;
    }

    private static async Task DeliverAsync(LifecycleSmtp smtp, MimeMessage message, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SendTimeout);
        using var client = new SmtpClient { Timeout = (int)SendTimeout.TotalMilliseconds, CheckCertificateRevocation = smtp.CheckCertificateRevocation };
        await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, timeoutCts.Token);
        if (!string.IsNullOrWhiteSpace(smtp.Username)) await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, timeoutCts.Token);
        await client.SendAsync(message, timeoutCts.Token);
        await client.DisconnectAsync(true, timeoutCts.Token);
    }

    private static string DeliveryError(Exception ex) =>
        ex is AuthenticationException ? "SMTP kullanıcı adı/şifre reddedildi." : $"Gönderilemedi: {ex.Message}";

    private static string StepLabel(LifecycleStep step) => step switch
    {
        LifecycleStep.Welcome => "Hoş geldin",
        LifecycleStep.CheckIn => "Nasıl gidiyor",
        LifecycleStep.TrialEnded => "Deneme bitti hatırlatması",
        _ => step.ToString(),
    };

    // ── Scheduler cycle ──────────────────────────────────────────────────────

    public async Task<LifecycleRunSummary> RunDueAsync(CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow; var notes = new List<string>();
        int candidates = 0, sent = 0, failed = 0, deferred = 0;
        try
        {
            if (!Enabled) { notes.Add("LifecycleMail:Enabled=false"); return Finish(); }
            if (!SmtpConfigured) { notes.Add("SMTP ayarı yok"); return Finish(); }
            var now = DateTime.UtcNow; var o = Options;
            var customers = await Scope(now).ToListAsync(cancellationToken);
            var sentMap = await SentStepsAsync(customers.Select(c => c.Id), cancellationToken);
            var due = customers
                .Select(c => (Customer: c, Step: DemoLifecyclePlanner.NextStep(Project(c), sentMap.GetValueOrDefault(c.Id) ?? new HashSet<LifecycleStep>(), now, o)))
                .Where(x => x.Step is not null).ToList();
            candidates = due.Count;
            foreach (var (c, step) in due)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!DemoLifecyclePlanner.InSendWindow(now, c.Country, o)) { deferred++; continue; }
                var error = await SendAsync(c.Id, step!.Value, force: true, triggeredBy: "auto", cancellationToken);
                if (error is null) sent++; else { failed++; notes.Add($"{c.CompanyName}: {error}"); }
                try { await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken); } catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "Lifecycle döngüsü hatası"); notes.Add(ex.Message); }
        return Finish();

        LifecycleRunSummary Finish()
        {
            var summary = new LifecycleRunSummary(started, DateTime.UtcNow, candidates, sent, failed, deferred, notes);
            _state.LastRun = summary;
            if (candidates > 0) _logger.LogInformation("Lifecycle: candidates={C} sent={S} failed={F} deferred={D}", candidates, sent, failed, deferred);
            return summary;
        }
    }
}

public sealed class DemoLifecycleState : IDemoLifecycleState
{
    public LifecycleRunSummary? LastRun { get; set; }
}
