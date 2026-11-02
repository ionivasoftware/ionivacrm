namespace IonCrm.Application.Features.Lifecycle;

/// <summary>Resolved outgoing-mail settings for the lifecycle e-mails, plus which config block they came from.</summary>
public sealed record LifecycleSmtp(string Source, string Host, int Port, bool UseSsl, string? Username, string? Password, string From)
{
    /// <summary>
    /// TLS certificate revocation check (CRL/OCSP) during the SMTP handshake. Default on. <c>LifecycleMail:Smtp:CheckCertificateRevocation=false</c>
    /// turns it off — needed for local runs on macOS, where .NET reports "incomplete certificate revocation check" for Gmail; the
    /// chain and host name are still validated.
    /// </summary>
    public bool CheckCertificateRevocation { get; init; } = true;

    public const string SourceLifecycle = "LifecycleMail";
    public const string SourceEmailCollector = "EmailCollector";
    public const string SourceNotifications = "Notifications";
}

/// <summary>
/// Picks the SMTP account the demo mails go out from. Order:
/// <list type="number">
/// <item><c>LifecycleMail:Smtp:*</c> when its Host is set (explicit, wins outright).</item>
/// <item><c>EmailCollector:Imap:*</c> — the support mailbox the invoice collector already reads; its IMAP host is
/// mapped to the provider's SMTP host (imap.gmail.com → smtp.gmail.com), 587/STARTTLS, same user/password, From = user.</item>
/// <item><c>Notifications:Smtp:*</c> — the ops alert account, last resort.</item>
/// </list>
/// On a derived base (2, 3) the single keys <c>LifecycleMail:Smtp:Port/UseSsl/From</c> still override, so e.g. only the
/// From address can be pinned without repeating the credentials. Pure — unit-tested.
/// </summary>
public static class LifecycleSmtpResolver
{
    public static LifecycleSmtp? Resolve(Func<string, string?> cfg)
    {
        string? L(string key) => Nz(cfg($"LifecycleMail:Smtp:{key}"));
        string? N(string key) => Nz(cfg($"Notifications:Smtp:{key}"));
        string? I(string key) => Nz(cfg($"EmailCollector:Imap:{key}"));

        LifecycleSmtp? smtp = null;
        if (L("Host") is { } host)
            smtp = new(LifecycleSmtp.SourceLifecycle, host, Port(L("Port"), 587), Flag(L("UseSsl"), false), L("Username"), L("Password"), L("From") ?? L("Username") ?? string.Empty);
        else if (I("Host") is { } imapHost && I("Username") is { } user && I("Password") is { } password)
            smtp = new(LifecycleSmtp.SourceEmailCollector, SmtpHostFromImap(imapHost), 587, false, user, password, user);
        else if (N("Host") is { } nHost)
            smtp = new(LifecycleSmtp.SourceNotifications, nHost, Port(N("Port"), 587), Flag(N("UseSsl"), false), N("Username"), N("Password"), N("From") ?? N("Username") ?? string.Empty);

        if (smtp is null) return null;
        if (smtp.Source != LifecycleSmtp.SourceLifecycle)
            smtp = smtp with { Port = Port(L("Port"), smtp.Port), UseSsl = Flag(L("UseSsl"), smtp.UseSsl), From = L("From") ?? smtp.From };
        smtp = smtp with { CheckCertificateRevocation = Flag(L("CheckCertificateRevocation"), true) };
        return string.IsNullOrWhiteSpace(smtp.From) ? null : smtp;
    }

    /// <summary>imap.gmail.com → smtp.gmail.com, imap-mail.outlook.com → smtp-mail.outlook.com, outlook.office365.com → smtp.office365.com; anything else is assumed to serve both protocols (mail.example.com).</summary>
    public static string SmtpHostFromImap(string imapHost)
    {
        var host = imapHost.Trim();
        if (host.Equals("outlook.office365.com", StringComparison.OrdinalIgnoreCase)) return "smtp.office365.com";
        if (host.StartsWith("imap", StringComparison.OrdinalIgnoreCase)) return "smtp" + host[4..];
        return host;
    }

    private static string? Nz(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int Port(string? raw, int fallback) => int.TryParse(raw, out var p) && p is > 0 and < 65536 ? p : fallback;
    private static bool Flag(string? raw, bool fallback) => bool.TryParse(raw, out var b) ? b : fallback;
}
