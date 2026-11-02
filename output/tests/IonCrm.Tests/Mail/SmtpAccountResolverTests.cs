using FluentAssertions;
using IonCrm.Application.Common.Mail;

namespace IonCrm.Tests.Mail;

public class SmtpAccountResolverTests
{
    private const string Lifecycle = "LifecycleMail:Smtp";

    private static Func<string, string?> Cfg(params (string Key, string? Value)[] pairs)
    {
        var d = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        return k => d.TryGetValue(k, out var v) ? v : null;
    }

    private static readonly (string, string?)[] Collector =
    {
        ("EmailCollector:Imap:Host", "imap.gmail.com"), ("EmailCollector:Imap:Port", "993"),
        ("EmailCollector:Imap:Username", "support@example.com"), ("EmailCollector:Imap:Password", "app-pass"),
    };

    private static readonly (string, string?)[] Notifications =
    {
        ("Notifications:Smtp:Host", "smtp.gmail.com"), ("Notifications:Smtp:Port", "465"), ("Notifications:Smtp:UseSsl", "true"),
        ("Notifications:Smtp:Username", "alerts@example.com"), ("Notifications:Smtp:Password", "alerts-pass"), ("Notifications:Smtp:From", "alerts@example.com"),
    };

    [Fact]
    public void Explicit_prefix_block_wins_over_everything()
    {
        var r = SmtpAccountResolver.Resolve(Cfg(Collector.Concat(Notifications).Concat(new (string, string?)[]
        {
            ("LifecycleMail:Smtp:Host", "smtp.custom.io"), ("LifecycleMail:Smtp:Port", "2525"), ("LifecycleMail:Smtp:Username", "u"), ("LifecycleMail:Smtp:Password", "p"),
            ("LifecycleMail:Smtp:ImapHost", "imap.custom.io"),
        }).ToArray()), Lifecycle);
        r.Should().NotBeNull();
        r!.Source.Should().Be("LifecycleMail");
        r.Host.Should().Be("smtp.custom.io"); r.Port.Should().Be(2525); r.UseSsl.Should().BeFalse();
        r.Username.Should().Be("u"); r.Password.Should().Be("p"); r.From.Should().Be("u", "From defaults to the username");
        r.ImapHost.Should().Be("imap.custom.io"); r.ImapPort.Should().Be(993); r.HasImap.Should().BeTrue();
    }

    [Fact]
    public void Support_mailbox_from_email_collector_is_preferred_over_notifications_and_keeps_its_imap_side()
    {
        var r = SmtpAccountResolver.Resolve(Cfg(Collector.Concat(Notifications).ToArray()), Lifecycle);
        r.Should().NotBeNull();
        r!.Source.Should().Be(SmtpAccount.SourceEmailCollector);
        r.Host.Should().Be("smtp.gmail.com", "IMAP host mapped to the SMTP host");
        r.Port.Should().Be(587); r.UseSsl.Should().BeFalse("STARTTLS");
        r.Username.Should().Be("support@example.com"); r.Password.Should().Be("app-pass"); r.From.Should().Be("support@example.com");
        r.ImapHost.Should().Be("imap.gmail.com"); r.ImapPort.Should().Be(993); r.HasImap.Should().BeTrue();
    }

    [Theory]
    [InlineData("Outreach:Smtp", "Outreach")]
    [InlineData("Marketing:Smtp", "Marketing")]
    public void Other_features_resolve_the_same_shared_mailbox_under_their_own_prefix(string prefix, string sourceWhenExplicit)
    {
        SmtpAccountResolver.Resolve(Cfg(Collector), prefix)!.Source.Should().Be(SmtpAccount.SourceEmailCollector);
        var explicitBlock = SmtpAccountResolver.Resolve(Cfg(Collector.Concat(new (string, string?)[] { ($"{prefix}:Host", "smtp.x"), ($"{prefix}:From", "a@x") }).ToArray()), prefix)!;
        explicitBlock.Source.Should().Be(sourceWhenExplicit); explicitBlock.Host.Should().Be("smtp.x"); explicitBlock.HasImap.Should().BeFalse("no ImapHost given");
    }

    [Fact]
    public void Single_prefix_keys_override_the_derived_base()
    {
        var r = SmtpAccountResolver.Resolve(Cfg(Collector.Concat(new (string, string?)[]
        {
            ("LifecycleMail:Smtp:From", "hello@example.com"), ("LifecycleMail:Smtp:Port", "465"), ("LifecycleMail:Smtp:UseSsl", "true"),
        }).ToArray()), Lifecycle);
        r!.Source.Should().Be(SmtpAccount.SourceEmailCollector);
        r.From.Should().Be("hello@example.com"); r.Port.Should().Be(465); r.UseSsl.Should().BeTrue();
        r.Username.Should().Be("support@example.com", "credentials still come from the collector");
    }

    [Fact]
    public void Certificate_revocation_check_is_on_unless_switched_off()
    {
        SmtpAccountResolver.Resolve(Cfg(Collector), Lifecycle)!.CheckCertificateRevocation.Should().BeTrue();
        SmtpAccountResolver.Resolve(Cfg(Collector.Concat(new (string, string?)[] { ("LifecycleMail:Smtp:CheckCertificateRevocation", "false") }).ToArray()), Lifecycle)!
            .CheckCertificateRevocation.Should().BeFalse();
    }

    [Fact]
    public void Notifications_is_the_last_resort_without_imap_and_incomplete_collector_is_skipped()
    {
        var r = SmtpAccountResolver.Resolve(Cfg(Notifications.Concat(new (string, string?)[] { ("EmailCollector:Imap:Host", "imap.gmail.com") }).ToArray()), Lifecycle);
        r!.Source.Should().Be(SmtpAccount.SourceNotifications);
        r.Host.Should().Be("smtp.gmail.com"); r.Port.Should().Be(465); r.UseSsl.Should().BeTrue(); r.From.Should().Be("alerts@example.com");
        r.HasImap.Should().BeFalse();
    }

    [Fact]
    public void Nothing_configured_or_no_sender_address_means_null()
    {
        SmtpAccountResolver.Resolve(Cfg(), Lifecycle).Should().BeNull();
        SmtpAccountResolver.Resolve(Cfg(("LifecycleMail:Smtp:Host", "smtp.x")), Lifecycle).Should().BeNull("no From/Username");
        SmtpAccountResolver.Resolve(Cfg(("LifecycleMail:Smtp:Host", "  "), ("Notifications:Smtp:Host", "smtp.y"), ("Notifications:Smtp:From", "a@y")), Lifecycle).Should().NotBeNull("blank host is treated as unset");
    }

    [Theory]
    [InlineData("imap.gmail.com", "smtp.gmail.com")]
    [InlineData("IMAP.mail.yahoo.com", "smtp.mail.yahoo.com")]
    [InlineData("imap-mail.outlook.com", "smtp-mail.outlook.com")]
    [InlineData("outlook.office365.com", "smtp.office365.com")]
    [InlineData("mail.example.com", "mail.example.com")]
    public void Imap_host_maps_to_smtp_host(string imap, string smtp) => SmtpAccountResolver.SmtpHostFromImap(imap).Should().Be(smtp);
}
