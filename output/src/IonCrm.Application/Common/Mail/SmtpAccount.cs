namespace IonCrm.Application.Common.Mail;

/// <summary>
/// One outgoing-mail account resolved from configuration: which block it came from, SMTP coordinates, and the
/// IMAP side of the same mailbox when known (reply/bounce look-ups).
/// </summary>
public sealed record SmtpAccount(string Source, string Host, int Port, bool UseSsl, string? Username, string? Password, string From)
{
    public const string SourceEmailCollector = "EmailCollector";
    public const string SourceNotifications = "Notifications";

    /// <summary>
    /// TLS certificate revocation check (CRL/OCSP) during the handshake. Default on. <c>{prefix}:CheckCertificateRevocation=false</c>
    /// turns it off — needed for local runs on macOS, where .NET reports "incomplete certificate revocation check" for Gmail;
    /// the chain and host name are still validated.
    /// </summary>
    public bool CheckCertificateRevocation { get; init; } = true;

    /// <summary>IMAP host of the same mailbox; null when the block gives no way to read it.</summary>
    public string? ImapHost { get; init; }

    public int ImapPort { get; init; } = 993;

    public bool HasImap => !string.IsNullOrWhiteSpace(ImapHost) && !string.IsNullOrWhiteSpace(Username);
}

/// <summary>
/// Picks the company account a feature's mail goes out from. Order:
/// <list type="number">
/// <item><c>{overridePrefix}:*</c> (e.g. <c>LifecycleMail:Smtp</c>, <c>Outreach:Smtp</c>, <c>Marketing:Smtp</c>) when its Host is set — explicit, wins outright.</item>
/// <item><c>EmailCollector:Imap:*</c> — the support mailbox the invoice collector already reads; its IMAP host is mapped to the
/// provider's SMTP host (imap.gmail.com → smtp.gmail.com), 587/STARTTLS, same user/password, From = user, IMAP kept for inbox checks.</item>
/// <item><c>Notifications:Smtp:*</c> — the ops alert account, last resort (no IMAP).</item>
/// </list>
/// On a derived base (2, 3) the single keys <c>{overridePrefix}:Port/UseSsl/From</c> still override, so e.g. only the From
/// address can be pinned without repeating the credentials. Pure — unit-tested.
/// </summary>
public static class SmtpAccountResolver
{
    public static SmtpAccount? Resolve(Func<string, string?> cfg, string overridePrefix)
    {
        string? O(string key) => Nz(cfg($"{overridePrefix}:{key}"));
        string? N(string key) => Nz(cfg($"Notifications:Smtp:{key}"));
        string? I(string key) => Nz(cfg($"EmailCollector:Imap:{key}"));
        var overrideSource = overridePrefix.Split(':')[0];

        SmtpAccount? account = null;
        if (O("Host") is { } host)
            account = new(overrideSource, host, Port(O("Port"), 587), Flag(O("UseSsl"), false), O("Username"), O("Password"), O("From") ?? O("Username") ?? string.Empty)
            { ImapHost = O("ImapHost"), ImapPort = Port(O("ImapPort"), 993) };
        else if (I("Host") is { } imapHost && I("Username") is { } user && I("Password") is { } password)
            account = new(SmtpAccount.SourceEmailCollector, SmtpHostFromImap(imapHost), 587, false, user, password, user)
            { ImapHost = imapHost, ImapPort = Port(I("Port"), 993) };
        else if (N("Host") is { } nHost)
            account = new(SmtpAccount.SourceNotifications, nHost, Port(N("Port"), 587), Flag(N("UseSsl"), false), N("Username"), N("Password"), N("From") ?? N("Username") ?? string.Empty);

        if (account is null) return null;
        if (account.Source != overrideSource)
            account = account with { Port = Port(O("Port"), account.Port), UseSsl = Flag(O("UseSsl"), account.UseSsl), From = O("From") ?? account.From };
        account = account with { CheckCertificateRevocation = Flag(O("CheckCertificateRevocation"), true) };
        return string.IsNullOrWhiteSpace(account.From) ? null : account;
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
