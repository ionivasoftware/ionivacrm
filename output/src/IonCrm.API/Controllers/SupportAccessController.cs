using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Mail;
using IonCrm.Application.Features.SupportAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Public answer pages for support-access consent (link in the request e-mail). No session; token-only. GET shows the
/// request and two buttons; the answer itself is a POST so that mail scanners following the link cannot approve.
/// Pages are in the language the mail was sent in (stored on the request) and name only the support team — never the operator.
/// </summary>
[Route("api/v1/support-access")]
[AllowAnonymous]
public sealed class SupportAccessController : ControllerBase
{
    private readonly ISupportAccessService _service;
    private readonly IConfiguration _configuration;

    public SupportAccessController(ISupportAccessService service, IConfiguration configuration) { _service = service; _configuration = configuration; }

    /// <summary>Support mailbox shown on the pages (the address the mail came from); null when none is configured.</summary>
    private string? SupportEmail => SmtpAccountResolver.Resolve(k => _configuration[k], "SupportAccess:Smtp")?.From;

    /// <summary>The language the request mail went out in; older rows without it fall back to the customer-derived language.</summary>
    private static string LanguageOf(Domain.Entities.SupportAccessRequest r, Domain.Entities.Customer customer) =>
        SupportAccessRules.NormalizeMailLanguage(r.Language) ?? SupportAccessRules.ResolveLanguage(customer.Language, customer.Country, customer.Phone).Language;

    [HttpGet("{token}")]
    public async Task<IActionResult> Review(string token, CancellationToken cancellationToken = default)
    {
        var (r, customer) = await LoadAsync(token, cancellationToken);
        if (r is null || customer is null) return Html(SupportAccessMail.RenderResultPage("en", "Invalid link", "This link is not valid."), 404);
        var lang = LanguageOf(r, customer);
        if (SupportAccessRules.WhyCannotRespond(r, DateTime.UtcNow) is { } why)
            return Html(SupportAccessMail.RenderResultPage(lang, lang == "tr" ? "Bu bağlantı artık geçerli değil" : "This link is no longer valid", lang == "tr" ? why : Translate(why), SupportEmail), 410);
        var ctx = new SupportAccessMail.Context(lang, customer.CompanyName, customer.ContactName, r.EmsUserName, SupportEmail ?? string.Empty, r.Reason, r.ValidHours, string.Empty);
        var baseUrl = $"{Request.Scheme}://{Request.Host}/api/v1/support-access/{Uri.EscapeDataString(token)}";
        return Html(SupportAccessMail.RenderReviewPage(ctx, baseUrl + "/approve", baseUrl + "/decline"), 200);
    }

    [HttpPost("{token}/approve")]
    public Task<IActionResult> Approve(string token, CancellationToken cancellationToken = default) => AnswerAsync(token, true, cancellationToken);

    [HttpPost("{token}/decline")]
    public Task<IActionResult> Decline(string token, CancellationToken cancellationToken = default) => AnswerAsync(token, false, cancellationToken);

    private async Task<IActionResult> AnswerAsync(string token, bool approve, CancellationToken cancellationToken)
    {
        var (r, customer) = await LoadAsync(token, cancellationToken);
        if (r is null || customer is null) return Html(SupportAccessMail.RenderResultPage("en", "Invalid link", "This link is not valid."), 404);
        var lang = LanguageOf(r, customer);
        if (!await _service.RespondAsync(r, approve, cancellationToken))
        {
            var why = SupportAccessRules.WhyCannotRespond(r, DateTime.UtcNow) ?? string.Empty;
            return Html(SupportAccessMail.RenderResultPage(lang, lang == "tr" ? "Bu bağlantı artık geçerli değil" : "This link is no longer valid", lang == "tr" ? why : Translate(why), SupportEmail), 410);
        }
        var until = r.ExpiresAt is { } e ? TimeZoneInfo.ConvertTimeFromUtc(e, SafeZone(lang == "tr" ? "Europe/Istanbul" : "Europe/London")).ToString("dd.MM.yyyy HH:mm") : string.Empty;
        var (title, text) = (approve, lang) switch
        {
            (true, "tr") => ("İzin verildi", $"Teşekkürler. LiftDesk destek ekibi {r.ValidHours} saat boyunca ({until}'e kadar) destek erişimiyle giriş yapabilir. Oturum LiftDesk'te denetim kayıtlıdır."),
            (true, _) => ("Permission granted", $"Thank you. The LiftDesk support team can sign in with support access for {r.ValidHours} hours (until {until}). The session is audit-logged in LiftDesk."),
            (false, "tr") => ("İstek reddedildi", "Destek erişimi açılmayacak. Bir sorun yaşarsanız bu e-postayı yanıtlayarak bize ulaşabilirsiniz."),
            (false, _) => ("Request declined", "No support access will be opened. If you need anything, just reply to the e-mail."),
        };
        return Html(SupportAccessMail.RenderResultPage(lang, title, text, SupportEmail), 200);
    }

    private async Task<(Domain.Entities.SupportAccessRequest?, Domain.Entities.Customer?)> LoadAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return (null, null);
        var r = await _service.FindByTokenAsync(token.Trim(), cancellationToken);
        if (r is null) return (null, null);
        return (r, await _service.GetCustomerAsync(r.CustomerId, cancellationToken));
    }

    private static string Translate(string tr) => tr switch
    {
        "Bu isteğe zaten izin verilmiş." => "This request has already been approved.",
        "Bu istek zaten reddedilmiş." => "This request has already been declined.",
        "Bu isteğin yerini yeni bir istek aldı." => "A newer request has replaced this one.",
        "Bu isteğin süresi dolmuş." => "This request has expired.",
        _ => tr,
    };

    private static TimeZoneInfo SafeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { return TimeZoneInfo.Utc; }
    }

    private static ContentResult Html(string html, int status) => new() { StatusCode = status, ContentType = "text/html; charset=utf-8", Content = html };
}
