using IonCrm.Application.Common.Helpers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Mail;
using IonCrm.Application.Common.Models;
using IonCrm.Application.Features.SupportAccess;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Customer consent before a support login. The request goes out from the company support mailbox
/// (<see cref="SmtpAccountResolver"/>, prefix <c>SupportAccess:Smtp</c> → <c>EmailCollector:Imap</c> → <c>Notifications:Smtp</c>),
/// written in the firm's language (feed <c>language</c> → country → phone prefix → Turkish, operator may override) and
/// signed by the support team only — the operator's name/address never reach the customer (they stay on the request row
/// and in the timeline). The customer answers on a public tokenised page; request and answer are written to the timeline.
/// </summary>
public sealed class SupportAccessService : ISupportAccessService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(40);

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SupportAccessService> _logger;

    public SupportAccessService(ApplicationDbContext db, IConfiguration configuration, ICurrentUserService currentUser, ILogger<SupportAccessService> logger)
    {
        _db = db; _configuration = configuration; _currentUser = currentUser; _logger = logger;
    }

    private SmtpAccount? Smtp => SmtpAccountResolver.Resolve(k => _configuration[k], "SupportAccess:Smtp");

    // Own public root (SupportAccess:PublicBaseUrl — e.g. a liftdesk.app subdomain) before the API's shared one; short /consent/{token} path.
    private string PublicBaseUrl => SupportAccessLinks.ResolvePublicBaseUrl(k => _configuration[k]) ?? "https://ion-crm-api-production.up.railway.app";

    private string ReviewUrl(string token) => SupportAccessLinks.ReviewUrl(PublicBaseUrl, token);

    private static SupportAccessRequestDto ToDto(SupportAccessRequest r, DateTime now) => new(
        r.Id, r.CustomerId, r.EmsUserId, r.EmsUserName, r.To, r.Reason, r.Status.ToString(), r.RequestedBy, r.RequestedByName, r.RequestedAt, r.LinkExpiresAt,
        r.RespondedAt, r.ExpiresAt, r.ValidHours, SupportAccessRules.IsActive(r, now), SupportAccessRules.IsPending(r, now), r.UseCount, r.SendError, r.Language);

    public async Task<SupportAccessOverview> GetAsync(Guid customerId, string emsUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        var rows = await _db.SupportAccessRequests.AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.EmsUserId == emsUserId)
            .OrderByDescending(r => r.RequestedAt).Take(10).ToListAsync(cancellationToken);
        var active = rows.FirstOrDefault(r => SupportAccessRules.IsActive(r, now));
        var pending = rows.FirstOrDefault(r => SupportAccessRules.IsPending(r, now));
        var language = SupportAccessRules.ResolveLanguage(customer?.Language, customer?.Country, customer?.Phone);
        return new SupportAccessOverview(active is null ? null : ToDto(active, now), pending is null ? null : ToDto(pending, now),
            rows.Take(5).Select(r => ToDto(r, now)).ToList(), Smtp?.From, customer?.Email, language.Language, language.Source);
    }

    public async Task<Result<SupportAccessRequestDto>> RequestAsync(Guid customerId, string emsUserId, string? emsUserName, string to, string reason, int? validHours, string? language = null, CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null) return Result<SupportAccessRequestDto>.Failure("Müşteri bulunamadı.");
        if (string.IsNullOrWhiteSpace(emsUserId)) return Result<SupportAccessRequestDto>.Failure("Kullanıcı seçilmedi.");
        var trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length < SupportAccessRules.ReasonMin || trimmedReason.Length > SupportAccessRules.ReasonMax)
            return Result<SupportAccessRequestDto>.Failure($"Gerekçe {SupportAccessRules.ReasonMin}–{SupportAccessRules.ReasonMax} karakter olmalı.");
        var address = (to ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(address) || !MailboxAddress.TryParse(address, out var toAddress) || !toAddress.Address.Contains('@'))
            return Result<SupportAccessRequestDto>.Failure("Geçerli bir alıcı e-posta adresi girin.");
        if (Smtp is not { } smtp)
            return Result<SupportAccessRequestDto>.Failure("Gönderici posta kutusu tanımlı değil (EmailCollector__Imap__* ya da SupportAccess__Smtp__*).");

        var operatorEmail = (_currentUser.Email ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(operatorEmail)) return Result<SupportAccessRequestDto>.Failure("Operatör kimliği belirlenemedi; yeniden giriş yapın.");
        var operatorUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _currentUser.UserId, cancellationToken);
        var operatorName = operatorUser?.FullName is { Length: > 0 } n ? n : operatorEmail;

        var now = DateTime.UtcNow;
        // The customer is written to in the firm's own language; the operator may have picked one in the dialog.
        var choice = SupportAccessRules.ResolveLanguage(customer.Language, customer.Country, customer.Phone, language);
        var lang = choice.Language;
        var request = new SupportAccessRequest
        {
            CustomerId = customer.Id, ProjectId = customer.ProjectId, EmsUserId = emsUserId, EmsUserName = emsUserName?.Trim(),
            To = toAddress.Address, Reason = trimmedReason, Token = SupportAccessRules.NewToken(), Language = lang,
            RequestedByUserId = _currentUser.UserId, RequestedBy = operatorEmail, RequestedByName = operatorName,
            ValidHours = SupportAccessRules.ClampHours(validHours), RequestedAt = now, LinkExpiresAt = now.AddDays(SupportAccessRules.LinkValidDays),
        };

        // Anonymous towards the customer: the support team asks, From/Reply-To is the support mailbox, no operator name or address in the mail.
        var rendered = SupportAccessMail.RenderRequest(new SupportAccessMail.Context(lang, customer.CompanyName, customer.ContactName, request.EmsUserName, smtp.From, trimmedReason, request.ValidHours, ReviewUrl(request.Token)));
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(lang == "tr" ? "LiftDesk Destek" : "LiftDesk Support", smtp.From));
        message.To.Add(toAddress);
        message.Subject = rendered.Subject;
        message.MessageId = MimeUtils.GenerateMessageId(smtp.From.Split('@').Last());
        message.Body = new BodyBuilder { HtmlBody = rendered.Html }.ToMessageBody();
        try
        {
            await SmtpDelivery.SendAsync(smtp, message, SendTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Destek erişimi izin maili gönderilemedi: müşteri {CustomerId} → {To}", customer.Id, toAddress.Address);
            return Result<SupportAccessRequestDto>.Failure(SmtpDelivery.Explain(ex, smtp));
        }
        request.MessageId = message.MessageId;

        // A new request replaces older unanswered ones for the same user — one live link at a time.
        var stale = await _db.SupportAccessRequests.Where(r => r.CustomerId == customer.Id && r.EmsUserId == emsUserId && r.Status == SupportAccessStatus.Pending).ToListAsync(cancellationToken);
        foreach (var s in stale) { s.Status = SupportAccessStatus.Cancelled; s.UpdatedAt = now; }

        _db.SupportAccessRequests.Add(request);
        _db.ContactHistories.Add(new ContactHistory
        {
            CustomerId = customer.Id, ProjectId = customer.ProjectId, Type = ContactType.Email,
            Subject = $"Destek erişimi izni istendi: {request.EmsUserName ?? emsUserId}",
            Content = $"Alıcı: {toAddress.Address}\nDil: {lang} ({choice.Source})\nGerekçe: {trimmedReason}\nGeçerlilik: izin sonrası {request.ValidHours} saat\nİsteyen: {operatorName} ({operatorEmail})",
            ContactedAt = now, CreatedByUserId = _currentUser.UserId == Guid.Empty ? null : _currentUser.UserId, Reached = true,
        });
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Destek erişimi izni istendi: {Operator} → müşteri {CustomerId} kullanıcı {EmsUserId} ({To}), {Hours} saat, dil {Lang} ({Source})", operatorEmail, customer.Id, emsUserId, toAddress.Address, request.ValidHours, lang, choice.Source);
        return Result<SupportAccessRequestDto>.Success(ToDto(request, now));
    }

    public async Task<string?> ValidateForLoginAsync(Guid requestId, Guid customerId, string emsUserId, CancellationToken cancellationToken = default)
    {
        var r = await _db.SupportAccessRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (r is null) return "İzin isteği bulunamadı.";
        return SupportAccessRules.WhyCannotLogin(r, customerId, emsUserId, DateTime.UtcNow);
    }

    public async Task MarkUsedAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var r = await _db.SupportAccessRequests.FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (r is null) return;
        var now = DateTime.UtcNow;
        r.FirstUsedAt ??= now; r.UseCount++; r.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<SupportAccessRequest?> FindByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        _db.SupportAccessRequests.IgnoreQueryFilters().FirstOrDefaultAsync(r => !r.IsDeleted && r.Token == token, cancellationToken);

    public Task<Customer?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        _db.Customers.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken);

    public async Task<bool> RespondAsync(SupportAccessRequest request, bool approve, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (!SupportAccessRules.Respond(request, approve, now)) return false;
        _db.ContactHistories.Add(new ContactHistory
        {
            CustomerId = request.CustomerId, ProjectId = request.ProjectId, Type = ContactType.Note,
            Subject = approve ? $"Destek erişimine izin verildi: {request.EmsUserName ?? request.EmsUserId}" : $"Destek erişimi reddedildi: {request.EmsUserName ?? request.EmsUserId}",
            Content = approve ? $"Müşteri {request.To} adresinden izin verdi. Geçerlilik: {request.ExpiresAt:dd.MM.yyyy HH:mm} UTC'ye kadar ({request.ValidHours} saat). İsteyen: {request.RequestedByName}." : $"Müşteri {request.To} adresinden reddetti. İsteyen: {request.RequestedByName}.",
            ContactedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Destek erişimi izni yanıtlandı: müşteri {CustomerId} kullanıcı {EmsUserId} → {Answer}", request.CustomerId, request.EmsUserId, approve ? "izin" : "ret");
        return true;
    }
}
