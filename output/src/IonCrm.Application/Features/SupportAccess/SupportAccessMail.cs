using System.Net;

namespace IonCrm.Application.Features.SupportAccess;

/// <summary>
/// Request e-mail and the public answer pages for support-access consent (Turkish / English). Pure — unit-tested.
/// The customer sees the company support team only: no operator name or address appears in the mail or on the pages
/// (the operator identity stays in the CRM timeline and audit rows); the only address shown is the support mailbox.
/// </summary>
public static class SupportAccessMail
{
    public record Rendered(string Subject, string Html);

    /// <param name="SupportEmail">The shared support mailbox the mail is sent from (signature + mailto); replies go there.</param>
    public record Context(string Language, string CompanyName, string? ContactName, string? EmsUserName, string SupportEmail, string Reason, int ValidHours, string ReviewUrl);

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string Team(bool tr) => tr ? "LiftDesk destek ekibi" : "The LiftDesk support team";

    private static string UserPhrase(Context c, bool tr) => string.IsNullOrWhiteSpace(c.EmsUserName)
        ? (tr ? "hesabınıza" : "your account")
        : (tr ? $"<b>{E(c.EmsUserName)}</b> kullanıcınıza" : $"the user <b>{E(c.EmsUserName)}</b>");

    private static string Intro(Context c, bool tr) => tr
        ? $"{Team(tr)}, <b>{E(c.CompanyName)}</b> LiftDesk hesabında {UserPhrase(c, tr)} destek erişimiyle giriş yapmak için izninizi istiyor."
        : $"{Team(tr)} is asking for your permission to sign in to {UserPhrase(c, tr)} on the <b>{E(c.CompanyName)}</b> LiftDesk account with support access.";

    /// <summary>The e-mail: one button to the review page (never a direct approve link — mail scanners follow links).</summary>
    public static Rendered RenderRequest(Context c)
    {
        var tr = c.Language == "tr";
        var greet = string.IsNullOrWhiteSpace(c.ContactName) ? (tr ? $"Sayın {E(c.CompanyName)} Yetkilisi" : $"Dear {E(c.CompanyName)} team") : (tr ? $"Sayın {E(c.ContactName)}" : $"Dear {E(c.ContactName)}");
        var subject = tr ? $"LiftDesk destek erişimi için izin isteği — {c.CompanyName}" : $"Permission request for LiftDesk support access — {c.CompanyName}";
        var why = tr ? "Gerekçe" : "Reason";
        var terms = tr
            ? $"İzin verirseniz erişim <b>{c.ValidHours} saat</b> boyunca geçerli olur. Oturum LiftDesk'te denetim kayıtlıdır; parola değiştirme, hesap silme, imza ve ödeme işlemleri bu oturumda engellidir. Bu isteği siz talep etmediyseniz “Reddet” demeniz yeterlidir."
            : $"If you approve, access stays valid for <b>{c.ValidHours} hours</b>. The session is audit-logged in LiftDesk; changing passwords, deleting the account, signatures and payments are blocked in it. If you did not ask for this, simply decline.";
        var button = tr ? "İsteği görüntüle ve yanıtla" : "Review and answer";
        var fallback = tr ? "Düğme çalışmazsa bu bağlantıyı tarayıcınıza yapıştırın:" : "If the button does not work, paste this link into your browser:";
        var questions = tr ? "Sorunuz olursa bu e-postayı yanıtlamanız yeterlidir." : "Questions? Just reply to this e-mail.";
        var sign = tr ? "LiftDesk Destek" : "LiftDesk Support";
        var html = $@"<!doctype html><html lang=""{(tr ? "tr" : "en")}""><head><meta charset=""utf-8""/><meta name=""viewport"" content=""width=device-width,initial-scale=1""/><title>{E(subject)}</title></head>
<body style=""margin:0;padding:0;background:#F4F5F7;"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#F4F5F7;""><tr><td align=""center"" style=""padding:28px 12px;"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" style=""width:600px;max-width:100%;background:#FFFFFF;border:1px solid #E5E9EF;"">
<tr><td style=""padding:28px 28px 0 28px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#5C6B80;"">
<div>{greet},</div>
<div style=""padding-top:12px;"">{Intro(c, tr)}</div>
<div style=""padding-top:12px;""><span style=""color:#0F1F33;font-weight:bold;"">{why}:</span> {E(c.Reason)}</div>
<div style=""padding-top:12px;"">{terms}</div>
</td></tr>
<tr><td style=""padding:20px 28px 0 28px;""><table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><td style=""background:#0B1B2B;""><a href=""{c.ReviewUrl}"" style=""display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;font-weight:bold;color:#FFFFFF;text-decoration:none;"">{button}</a></td></tr></table></td></tr>
<tr><td style=""padding:16px 28px 28px 28px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#8C99A9;"">{fallback}<br/><a href=""{c.ReviewUrl}"" style=""color:#0284C7;"">{c.ReviewUrl}</a>
<div style=""padding-top:10px;"">{questions}</div>
<div style=""border-top:1px solid #E5E9EF;margin-top:16px;padding-top:12px;font-size:13px;color:#5C6B80;"">{sign} · <a href=""mailto:{E(c.SupportEmail)}"" style=""color:#0284C7;text-decoration:none;"">{E(c.SupportEmail)}</a></div></td></tr>
</table>
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" style=""width:600px;max-width:100%;""><tr><td style=""padding:14px 8px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:17px;color:#8C99A9;"">İYON YAZILIM DAN. BİL. TEK. SAN. TİC. LTD. ŞTİ. (IONIVA) · Kozyatağı Mah. Sarıkanarya Sok. No:14/1, Kadıköy, İstanbul, Türkiye</td></tr></table>
</td></tr></table></body></html>";
        return new Rendered(subject, html);
    }

    /// <summary>The public review page: details + two POST buttons (approve / decline).</summary>
    public static string RenderReviewPage(Context c, string approveUrl, string declineUrl)
    {
        var tr = c.Language == "tr";
        var title = tr ? "LiftDesk destek erişimi izni" : "LiftDesk support access permission";
        var terms = tr
            ? $"İzin verirseniz erişim <b>{c.ValidHours} saat</b> boyunca geçerli olur. Oturum LiftDesk'te denetim kayıtlıdır; parola değiştirme, hesap silme, imza ve ödeme işlemleri engellidir."
            : $"If you approve, access stays valid for <b>{c.ValidHours} hours</b>. The session is audit-logged in LiftDesk; password changes, account deletion, signatures and payments are blocked.";
        var approve = tr ? $"İzin veriyorum ({c.ValidHours} saat)" : $"I approve ({c.ValidHours} hours)";
        var decline = tr ? "Reddet" : "Decline";
        var why = tr ? "Gerekçe" : "Reason";
        return Shell(tr, title, $@"<p style=""font-size:14px;line-height:22px;color:#5C6B80;margin:0 0 12px;"">{Intro(c, tr)}</p>
<p style=""font-size:14px;line-height:22px;color:#5C6B80;margin:0 0 12px;""><b style=""color:#0F1F33;"">{why}:</b> {E(c.Reason)}</p>
<p style=""font-size:14px;line-height:22px;color:#5C6B80;margin:0 0 20px;"">{terms}</p>
<form method=""post"" action=""{approveUrl}"" style=""display:inline-block;margin:0 8px 8px 0;""><button type=""submit"" style=""background:#0B1B2B;color:#fff;border:0;padding:12px 22px;font-size:14px;font-weight:bold;cursor:pointer;"">{approve}</button></form>
<form method=""post"" action=""{declineUrl}"" style=""display:inline-block;margin:0 0 8px 0;""><button type=""submit"" style=""background:#fff;color:#0F1F33;border:1px solid #CBD2DC;padding:12px 22px;font-size:14px;cursor:pointer;"">{decline}</button></form>", c.SupportEmail);
    }

    public static string RenderResultPage(string language, string title, string text, string? supportEmail = null) =>
        Shell(language == "tr", title, $@"<p style=""font-size:14px;line-height:22px;color:#5C6B80;margin:0;"">{text}</p>", supportEmail);

    private static string Shell(bool tr, string title, string body, string? supportEmail)
    {
        var contact = string.IsNullOrWhiteSpace(supportEmail) ? string.Empty : $@" · <a href=""mailto:{E(supportEmail)}"" style=""color:#0284C7;text-decoration:none;"">{E(supportEmail)}</a>";
        return $@"<!doctype html><html lang=""{(tr ? "tr" : "en")}""><head><meta charset=""utf-8""/><meta name=""viewport"" content=""width=device-width,initial-scale=1""/><title>{E(title)}</title></head>
<body style=""margin:0;background:#F4F5F7;font-family:Arial,Helvetica,sans-serif;color:#0F1F33;""><div style=""max-width:560px;margin:48px auto;background:#fff;border:1px solid #E5E9EF;padding:32px;"">
<h1 style=""font-size:20px;margin:0 0 16px;"">{E(title)}</h1>{body}
<p style=""font-size:12px;color:#8C99A9;margin:24px 0 0;"">{(tr ? "LiftDesk Destek" : "LiftDesk Support")} by IONIVA{contact}</p></div></body></html>";
    }
}
