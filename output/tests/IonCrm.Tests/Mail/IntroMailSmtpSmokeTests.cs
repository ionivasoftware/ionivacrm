using FluentAssertions;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit.Abstractions;

namespace IonCrm.Tests.Mail;

/// <summary>
/// Opt-in end-to-end check of the UK outreach intro (OutreachService, step 0) through the SHARED company mailbox — no
/// personal Gmail on the test user. The Turkish intro mail is not covered here any more: it goes out from the operator's
/// own Gmail (Ayarlar → E-posta Gönderimi → "Test maili gönder" exercises that path). Skipped unless <c>INTRO_SMOKE_TO</c>
/// is set, so CI never sends mail. Run with the production variables injected, e.g.
/// <c>INTRO_SMOKE_TO=you@example.com railway run --service ion-crm-api --environment production -- dotnet test --no-build --filter IntroMailSmtpSmoke</c>.
/// On macOS add <c>Outreach__Smtp__CheckCertificateRevocation=false</c>.
/// </summary>
public class IntroMailSmtpSmokeTests
{
    private readonly ITestOutputHelper _out;
    public IntroMailSmtpSmokeTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Sends_uk_intro_from_the_shared_mailbox()
    {
        var to = Environment.GetEnvironmentVariable("INTRO_SMOKE_TO");
        if (string.IsNullOrWhiteSpace(to)) { _out.WriteLine("INTRO_SMOKE_TO not set — skipped."); return; }

        var pairs = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Select(e => new KeyValuePair<string, string?>(e.Key.ToString()!.Replace("__", ":"), e.Value?.ToString()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

        var current = new Mock<ICurrentUserService>();
        current.Setup(u => u.IsSuperAdmin).Returns(true);
        current.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, current.Object);

        // Approver without a personal Gmail: the shared mailbox must carry the mail; replies go to that mailbox.
        var user = new User { Id = Guid.NewGuid(), Email = to, FirstName = "Ömer", LastName = "Çakmakcı", PasswordHash = "x", MailBccSelf = false };
        db.Users.Add(user);
        var lead = new GlobalLead { Id = Guid.NewGuid(), Country = "GB", CompanyName = "Apex Lifts Ltd", NormalizedName = "apex lifts", ContactName = "Jane Carter", Email = to, Source = "smoke", Fingerprint = "GB:smoke-" + Guid.NewGuid().ToString("N"), Status = GlobalLeadStatus.Approved, OutreachStatus = OutreachStatus.Active, SenderUserId = user.Id };
        db.GlobalLeads.Add(lead);
        await db.SaveChangesAsync();

        var outreach = new OutreachService(db, new GlobalLeadRepository(db), new Mock<ITokenProtector>().Object, configuration, new OutreachState(), NullLogger<OutreachService>.Instance);
        outreach.SharedSender.Should().NotBeNull("the shared mailbox must be configured for this smoke test");
        var err = await outreach.SendNextStepAsync(lead);
        _out.WriteLine($"outreach intro → {to} via {outreach.SharedSender!.Source} from {outreach.SharedSender.Address} (inbox check {outreach.SharedSender.InboxCheck}): {(err is null ? "OK — step " + lead.OutreachStep + ", next " + lead.NextSendAt : "FAILED — " + err)}");
        err.Should().BeNull();
        lead.OutreachStep.Should().Be(1);
    }
}
