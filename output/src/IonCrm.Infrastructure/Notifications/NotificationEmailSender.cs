using IonCrm.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace IonCrm.Infrastructure.Notifications;

/// <summary>
/// MailKit SMTP implementation of <see cref="INotificationEmailSender"/>.
///
/// Config (Railway env karşılığı çift alt çizgi ile):
///   Notifications:Smtp:Host / Port (587) / Username / Password / UseSsl (false=STARTTLS)
///   Notifications:Smtp:From / FromName
///   Notifications:Recipients  — virgül veya noktalı virgülle ayrılmış liste
///   Notifications:Environment — konu önüne eklenen etiket (ör. "PROD")
///
/// Gönderim BEST-EFFORT: hiçbir koşulda çağıranın işlemini bozmaz. Kısa bir zaman aşımı uygulanır,
/// çünkü bu çağrı kullanıcının beklediği bir HTTP isteğinin (süre uzatma/SMS) içinden de yapılıyor;
/// SMTP takılırsa operatör işlemi bitmiş olmasına rağmen bekler.
/// </summary>
public sealed class NotificationEmailSender : INotificationEmailSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationEmailSender> _logger;

    /// <summary>Initialises a new instance of <see cref="NotificationEmailSender"/>.</summary>
    public NotificationEmailSender(IConfiguration configuration, ILogger<NotificationEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string? Host => _configuration["Notifications:Smtp:Host"];
    private string? Username => _configuration["Notifications:Smtp:Username"];
    private string? Password => _configuration["Notifications:Smtp:Password"];

    private string From =>
        _configuration["Notifications:Smtp:From"]
        ?? Username
        ?? "no-reply@ioniva.local";

    /// <summary>
    /// Alıcılar tek bir string'de tutulur: Railway ortam değişkenleri dizi taşıyamadığı için
    /// virgül/noktalı virgül ayrımı en pratik biçim.
    /// </summary>
    private List<string> Recipients =>
        (_configuration["Notifications:Recipients"] ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && Recipients.Count > 0;

    /// <inheritdoc />
    public async Task<bool> SendAsync(string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Bildirim e-postası atlandı — SMTP host veya alıcı tanımlı değil. Konu: {Subject}", subject);
            return false;
        }

        var recipients = Recipients;
        var port     = _configuration.GetValue("Notifications:Smtp:Port", 587);
        var useSsl   = _configuration.GetValue("Notifications:Smtp:UseSsl", false);
        var fromName = _configuration["Notifications:Smtp:FromName"] ?? "ION CRM";
        var envTag   = _configuration["Notifications:Environment"];

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, From));
        foreach (var to in recipients)
            message.To.Add(MailboxAddress.Parse(to));

        message.Subject = string.IsNullOrWhiteSpace(envTag) ? subject : $"[{envTag}] {subject}";
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        // Çağıranın iptal token'ı ile kendi zaman aşımımızı birleştiriyoruz: HTTP isteği iptal
        // olursa hemen çıkılır, olmazsa da SMTP bizi süresiz bekletemez.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SendTimeout);

        try
        {
            using var client = new SmtpClient { Timeout = (int)SendTimeout.TotalMilliseconds };
            await client.ConnectAsync(Host!, port,
                useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, timeoutCts.Token);

            if (!string.IsNullOrWhiteSpace(Username))
                await client.AuthenticateAsync(Username, Password ?? string.Empty, timeoutCts.Token);

            await client.SendAsync(message, timeoutCts.Token);
            await client.DisconnectAsync(true, timeoutCts.Token);

            _logger.LogInformation("Bildirim e-postası gönderildi ({Count} alıcı). Konu: {Subject}",
                recipients.Count, subject);
            return true;
        }
        catch (Exception ex)
        {
            // Bilinçli olarak yutuluyor: bildirim gönderilemedi diye uzatma/SMS/yedek kontrolü
            // başarısız sayılmamalı.
            _logger.LogWarning(ex, "Bildirim e-postası gönderilemedi. Konu: {Subject}", subject);
            return false;
        }
    }
}
