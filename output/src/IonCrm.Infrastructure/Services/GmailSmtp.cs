using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace IonCrm.Infrastructure.Services;

/// <summary>Shared Gmail SMTP send (smtp.gmail.com:587 STARTTLS, app password). Throws MailKit exceptions; callers map them.</summary>
public static class GmailSmtp
{
    public const string Host = "smtp.gmail.com";
    public const int Port = 587;
    public const string ImapHost = "imap.gmail.com";
    public const int ImapPort = 993;

    public static async Task SendAsync(string address, string appPassword, MimeMessage message, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var client = new SmtpClient { Timeout = (int)timeout.TotalMilliseconds };
        await client.ConnectAsync(Host, Port, SecureSocketOptions.StartTls, timeoutCts.Token);
        await client.AuthenticateAsync(address, appPassword, timeoutCts.Token);
        await client.SendAsync(message, timeoutCts.Token);
        await client.DisconnectAsync(true, timeoutCts.Token);
    }

    /// <summary>Operator-facing Turkish explanation for the common failure classes.</summary>
    public static string Explain(Exception ex) => ex switch
    {
        AuthenticationException => "Gmail kullanıcı adı/şifreyi kabul etmedi. 2 Adımlı Doğrulama açık olmalı ve 16 haneli Uygulama Şifresi kullanılmalı (Google Hesabı → Güvenlik → Uygulama şifreleri).",
        OperationCanceledException => "Gmail zaman aşımına uğradı. Tekrar deneyin.",
        _ => $"Gönderilemedi: {ex.Message}",
    };
}
