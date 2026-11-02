using FluentAssertions;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit.Abstractions;

namespace IonCrm.Tests.Lifecycle;

/// <summary>
/// Opt-in end-to-end check of the real send path (resolver → template → MailKit → SMTP). Skipped unless
/// <c>LIFECYCLE_SMOKE_TO</c> is set, so CI never sends mail. Run with the production variables injected, e.g.
/// <c>LIFECYCLE_SMOKE_TO=you@example.com railway run --service ion-crm-api --environment production -- dotnet test --no-build --filter LifecycleSmtpSmoke</c>.
/// Optional: <c>LIFECYCLE_SMOKE_STEPS=Welcome,CheckIn</c> (default all), <c>LIFECYCLE_SMOKE_LANG=en</c>.
/// On macOS add <c>LifecycleMail__Smtp__CheckCertificateRevocation=false</c> — .NET there cannot complete Gmail's revocation
/// check and MailKit refuses the handshake ("incomplete certificate revocation check"); the Linux container has no such issue.
/// </summary>
public class LifecycleSmtpSmokeTests
{
    private readonly ITestOutputHelper _out;
    public LifecycleSmtpSmokeTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Sends_each_step_to_the_opt_in_address()
    {
        var to = Environment.GetEnvironmentVariable("LIFECYCLE_SMOKE_TO");
        if (string.IsNullOrWhiteSpace(to)) { _out.WriteLine("LIFECYCLE_SMOKE_TO not set — skipped."); return; }

        // Process environment → configuration, the way the host does it (double underscore = section separator).
        var pairs = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Select(e => new KeyValuePair<string, string?>(e.Key.ToString()!.Replace("__", ":"), e.Value?.ToString()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.IsSuperAdmin).Returns(true);
        user.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, user.Object);
        var service = new DemoLifecycleService(db, configuration, new DemoLifecycleState(), NullLogger<DemoLifecycleService>.Instance);

        var lang = Environment.GetEnvironmentVariable("LIFECYCLE_SMOKE_LANG") is { Length: > 0 } l ? l : "en";
        var steps = (Environment.GetEnvironmentVariable("LIFECYCLE_SMOKE_STEPS") ?? "Welcome,CheckIn,TrialEnded")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.Parse<LifecycleStep>(s, true)).ToList();

        foreach (var step in steps)
        {
            var r = await service.SendTestAsync(to, step, lang, "smoke-test");
            _out.WriteLine($"{step} [{r.Language}] → {r.To} via {r.SmtpSource} from {r.From}: {(r.Error is null ? "OK — " + r.Subject : "FAILED — " + r.Error)}");
            r.Error.Should().BeNull($"{step} should be accepted by the SMTP server (source {r.SmtpSource}, from {r.From})");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
