using IonCrm.Application.Common.Mail;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace IonCrm.Infrastructure.Services;

/// <summary>One SMTP submission through a resolved <see cref="SmtpAccount"/> (MailKit). Throws; callers map with <see cref="Explain"/>.</summary>
public static class SmtpDelivery
{
    public static async Task SendAsync(SmtpAccount account, MimeMessage message, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var client = new SmtpClient { Timeout = (int)timeout.TotalMilliseconds, CheckCertificateRevocation = account.CheckCertificateRevocation };
        await client.ConnectAsync(account.Host, account.Port, account.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, timeoutCts.Token);
        if (!string.IsNullOrWhiteSpace(account.Username)) await client.AuthenticateAsync(account.Username, account.Password ?? string.Empty, timeoutCts.Token);
        await client.SendAsync(message, timeoutCts.Token);
        await client.DisconnectAsync(true, timeoutCts.Token);
    }

    /// <summary>Operator-facing Turkish explanation for the common failure classes.</summary>
    public static string Explain(Exception ex, SmtpAccount account) => ex switch
    {
        AuthenticationException => $"SMTP kullanıcı adı/şifre reddedildi ({account.From} — {account.Source} ayarı).",
        OperationCanceledException => "SMTP zaman aşımına uğradı. Tekrar deneyin.",
        _ => $"Gönderilemedi: {ex.Message}",
    };
}
