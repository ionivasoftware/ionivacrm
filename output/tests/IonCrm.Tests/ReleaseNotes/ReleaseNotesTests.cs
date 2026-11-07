using System.Net;
using System.Text;
using FluentAssertions;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.ReleaseNotes;
using IonCrm.Application.ReleaseNotes.Commands.MarkReleaseNotesSeen;
using IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotes;
using IonCrm.Application.ReleaseNotes.Queries.GetReleaseNotesStatus;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.ExternalApis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace IonCrm.Tests.ReleaseNotes;

/// <summary>Wire-level checks for the Liftdesk release-notes client: contract path, bearer, parsing, caching.</summary>
public class LiftdeskReleaseNotesClientTests
{
    private const string Payload = """
        {"success":true,"statusCode":200,"message":null,"data":{"language":"tr","generatedAt":"2026-11-07","releases":[
          {"id":"2026-10-31_2026-11-07","title":"Yenilikler ve İyileştirmeler","periodStart":"2026-10-31","periodEnd":"2026-11-07",
           "summary":"Son bir haftada eklenen özellikler.","sections":[{"key":"faults","title":"Arıza bildirimi ve acil durum","items":[
             {"type":"new","title":"Arıza bildirirken fotoğraf ve video eklenebiliyor.","note":"Görseller ofis ve saha ekranlarında görünüyor."},
             {"type":"fix","title":"Bir düzeltme.","note":null}]}]},
          {"id":"2026-10-24_2026-10-31","title":"Önceki","periodStart":"2026-10-24","periodEnd":"2026-10-31","summary":null,"sections":[]}]}}
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, Payload);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; LastRequest = request;
            return Task.FromResult(Respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (LiftdeskReleaseNotesClient Client, StubHandler Handler) Build(string? apiKey = "test-key")
    {
        var handler = new StubHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Liftdesk:ApiKey"] = apiKey,
            ["Liftdesk:BaseUrl"] = "https://ems.test/",
        }).Build();
        var client = new LiftdeskReleaseNotesClient(new HttpClient(handler), config, new MemoryCache(new MemoryCacheOptions()), NullLogger<LiftdeskReleaseNotesClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task Get_calls_the_contract_path_with_the_shared_bearer_and_parses_the_payload()
    {
        var (client, handler) = Build();

        var env = await client.GetAsync();

        env.Success.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://ems.test/api/v1/crm/release-notes");
        handler.LastRequest.Headers.Authorization!.ToString().Should().Be("Bearer test-key");

        env.Data!.Language.Should().Be("tr");
        env.Data.GeneratedAt.Should().Be("2026-11-07", "dates travel as plain yyyy-MM-dd text");
        env.Data.Releases.Should().HaveCount(2);
        var latest = env.Data.Releases![0];
        latest.Id.Should().Be("2026-10-31_2026-11-07");
        latest.PeriodEnd.Should().Be("2026-11-07");
        var section = latest.Sections.Should().ContainSingle().Subject;
        section.Key.Should().Be("faults");
        section.Items.Should().HaveCount(2);
        section.Items![0].Type.Should().Be("new");
        section.Items[0].Note.Should().StartWith("Görseller");
        section.Items[1].Note.Should().BeNull();
    }

    [Fact]
    public async Task Successful_payload_is_served_from_memory_but_failures_are_retried()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.Unauthorized, """{"success":false,"message":"Unauthorized","statusCode":401}""");

        var first = await client.GetAsync();
        first.Success.Should().BeFalse();
        first.StatusCode.Should().Be(401);
        (await client.GetAsync()).Success.Should().BeFalse();
        handler.Calls.Should().Be(2, "a failed answer is never cached");

        handler.Respond = _ => Json(HttpStatusCode.OK, Payload);
        (await client.GetAsync()).Success.Should().BeTrue();
        (await client.GetAsync()).Data!.Releases.Should().HaveCount(2);
        handler.Calls.Should().Be(3, "the good answer is cached for a while");
    }

    [Fact]
    public async Task Garbage_body_becomes_a_turkish_failure_envelope_and_missing_key_means_not_configured()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.OK, "<html>oops</html>");
        var env = await client.GetAsync();
        env.Success.Should().BeFalse();
        env.Message.Should().Contain("çözümlenemedi");

        Build(apiKey: "").Client.IsConfigured.Should().BeFalse();
        client.IsConfigured.Should().BeTrue();
    }
}

/// <summary>Pure unseen-count / badge rules (<see cref="ReleaseNotesStatus"/>).</summary>
public class ReleaseNotesStatusTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);
    private static LiftdeskRelease R(string id, string? end) => new(id, "T", null, end, null, null);

    [Fact]
    public void Unseen_is_the_number_of_releases_ahead_of_the_last_seen_one()
    {
        var releases = new[] { R("c", "2026-11-07"), R("b", "2026-10-31"), R("a", "2026-10-24") };
        ReleaseNotesStatus.UnseenCount(releases, "a", Today).Should().Be(2);
        ReleaseNotesStatus.UnseenCount(releases, "b", Today).Should().Be(1);
        ReleaseNotesStatus.UnseenCount(releases, "c", Today).Should().Be(0);
    }

    [Fact]
    public void Never_seen_or_unknown_id_counts_only_the_last_30_days_and_ignores_undated_releases()
    {
        var releases = new[] { R("d", "2026-11-07"), R("c", "2026-10-15"), R("b", "2026-10-01"), R("a", "not-a-date"), R("z", null) };
        ReleaseNotesStatus.UnseenCount(releases, null, Today).Should().Be(2);
        ReleaseNotesStatus.UnseenCount(releases, "pruned-id", Today).Should().Be(2);
        ReleaseNotesStatus.UnseenCount(Array.Empty<LiftdeskRelease>(), null, Today).Should().Be(0);
    }

    [Fact]
    public void Build_reads_the_latest_release_and_skips_blank_ids()
    {
        var data = new LiftdeskReleaseNotes("tr", "2026-11-07", new List<LiftdeskRelease> { R("", "2026-11-08"), R("x", "2026-11-07"), R("y", "2026-10-31") });

        var status = ReleaseNotesStatus.Build(data, "y", Today);

        status.Configured.Should().BeTrue();
        status.LatestReleaseId.Should().Be("x");
        status.LatestTitle.Should().Be("T");
        status.LatestPeriodEnd.Should().Be("2026-11-07");
        status.LastSeenReleaseId.Should().Be("y");
        status.UnseenCount.Should().Be(1);

        var empty = ReleaseNotesStatus.Build(null, null, Today, configured: false);
        (empty.Configured, empty.LatestReleaseId, empty.UnseenCount).Should().Be((false, null, 0));
    }
}

/// <summary>Handlers: proxy + per-user seen state (Users.LastSeenReleaseId).</summary>
public class ReleaseNotesHandlerTests
{
    private readonly Mock<ILiftdeskReleaseNotesClient> _client = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ICurrentUserService> _current = new();
    private readonly User _user = new() { Id = Guid.NewGuid(), Email = "ops@example.com", FirstName = "Op", LastName = "Er" };
    private static readonly string TodayText = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    public ReleaseNotesHandlerTests()
    {
        _client.Setup(c => c.IsConfigured).Returns(true);
        _current.Setup(c => c.IsAuthenticated).Returns(true);
        _current.Setup(c => c.UserId).Returns(_user.Id);
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    private static LiftdeskRelease R(string id, string end) => new(id, "Başlık " + id, null, end, null, null);
    private static LiftdeskEnvelope<LiftdeskReleaseNotes> Ok(params LiftdeskRelease[] releases) =>
        new(true, new LiftdeskReleaseNotes("tr", TodayText, releases.ToList()), null, null, 200);
    private static LiftdeskEnvelope<LiftdeskReleaseNotes> Fail(int code, string message) => new(false, null, message, null, code);

    [Fact]
    public async Task GetReleaseNotes_fails_when_key_is_missing_or_liftdesk_answers_with_an_error()
    {
        _client.Setup(c => c.IsConfigured).Returns(false);
        var notConfigured = await new GetReleaseNotesQueryHandler(_client.Object, _users.Object, _current.Object).Handle(new GetReleaseNotesQuery(), default);
        notConfigured.IsSuccess.Should().BeFalse();
        notConfigured.FirstError.Should().Be(GetReleaseNotesQueryHandler.NotConfiguredMessage);
        _client.Verify(c => c.GetAsync(It.IsAny<CancellationToken>()), Times.Never);

        _client.Setup(c => c.IsConfigured).Returns(true);
        _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Fail(503, "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil (503)."));
        var failed = await new GetReleaseNotesQueryHandler(_client.Object, _users.Object, _current.Object).Handle(new GetReleaseNotesQuery(), default);
        failed.IsSuccess.Should().BeFalse();
        failed.FirstError.Should().Contain("503");
    }

    [Fact]
    public async Task GetReleaseNotes_returns_releases_with_the_callers_seen_state()
    {
        _user.LastSeenReleaseId = "old";
        _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Ok(R("new", TodayText), R("old", "2026-10-31"), R("", "2026-10-24")));

        var result = await new GetReleaseNotesQueryHandler(_client.Object, _users.Object, _current.Object).Handle(new GetReleaseNotesQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Language.Should().Be("tr");
        result.Value.Releases.Select(r => r.Id).Should().Equal("new", "old");
        result.Value.LastSeenReleaseId.Should().Be("old");
        result.Value.UnseenCount.Should().Be(1);
    }

    [Fact]
    public async Task Status_stays_quiet_when_not_configured_or_liftdesk_is_down()
    {
        _client.Setup(c => c.IsConfigured).Returns(false);
        var off = await new GetReleaseNotesStatusQueryHandler(_client.Object, _users.Object, _current.Object).Handle(new GetReleaseNotesStatusQuery(), default);
        off.IsSuccess.Should().BeTrue();
        off.Value!.Configured.Should().BeFalse();
        off.Value.UnseenCount.Should().Be(0);
        _client.Verify(c => c.GetAsync(It.IsAny<CancellationToken>()), Times.Never);

        _user.LastSeenReleaseId = "seen";
        _client.Setup(c => c.IsConfigured).Returns(true);
        _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Fail(504, "timeout"));
        var down = await new GetReleaseNotesStatusQueryHandler(_client.Object, _users.Object, _current.Object).Handle(new GetReleaseNotesStatusQuery(), default);
        down.IsSuccess.Should().BeTrue("the sidebar badge must never surface a Liftdesk outage as an error");
        down.Value!.Configured.Should().BeTrue();
        down.Value.LatestReleaseId.Should().BeNull();
        down.Value.LastSeenReleaseId.Should().Be("seen");
        down.Value.UnseenCount.Should().Be(0);
    }

    [Fact]
    public async Task Status_reports_the_latest_release_and_the_unseen_count()
    {
        // "a" ended long before the 30-day window → a never-seen user is nudged only about "b".
        _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Ok(R("b", TodayText), R("a", "2026-09-01")));

        var result = await new GetReleaseNotesStatusQueryHandler(_client.Object, _users.Object, _current.Object).Handle(new GetReleaseNotesStatusQuery(), default);

        result.Value!.LatestReleaseId.Should().Be("b");
        result.Value.LatestTitle.Should().Be("Başlık b");
        result.Value.LatestPeriodEnd.Should().Be(TodayText);
        result.Value.LastSeenReleaseId.Should().BeNull();
        result.Value.UnseenCount.Should().Be(1, "never seen → only releases from the last 30 days count");
    }

    [Fact]
    public async Task MarkSeen_rejects_bad_ids_persists_a_valid_one_once_and_returns_the_refreshed_status()
    {
        _client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Ok(R("2026-10-31_2026-11-07", TodayText), R("2026-10-24_2026-10-31", "2026-10-31")));
        var handler = new MarkReleaseNotesSeenCommandHandler(_client.Object, _users.Object, _current.Object);

        var bad = await handler.Handle(new MarkReleaseNotesSeenCommand("drop table; --"), default);
        bad.IsSuccess.Should().BeFalse();
        _users.Verify(u => u.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);

        var ok = await handler.Handle(new MarkReleaseNotesSeenCommand(" 2026-10-31_2026-11-07 "), default);
        ok.IsSuccess.Should().BeTrue();
        _user.LastSeenReleaseId.Should().Be("2026-10-31_2026-11-07");
        ok.Value!.LastSeenReleaseId.Should().Be("2026-10-31_2026-11-07");
        ok.Value.UnseenCount.Should().Be(0);
        ok.Value.LatestReleaseId.Should().Be("2026-10-31_2026-11-07");
        _users.Verify(u => u.UpdateAsync(_user, It.IsAny<CancellationToken>()), Times.Once);

        await handler.Handle(new MarkReleaseNotesSeenCommand("2026-10-31_2026-11-07"), default);
        _users.Verify(u => u.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once, "same id again is a no-op write");
    }

    [Fact]
    public async Task MarkSeen_requires_an_authenticated_known_user()
    {
        _current.Setup(c => c.IsAuthenticated).Returns(false);
        var anon = await new MarkReleaseNotesSeenCommandHandler(_client.Object, _users.Object, _current.Object).Handle(new MarkReleaseNotesSeenCommand("x"), default);
        anon.IsSuccess.Should().BeFalse();

        _current.Setup(c => c.IsAuthenticated).Returns(true);
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var missing = await new MarkReleaseNotesSeenCommandHandler(_client.Object, _users.Object, _current.Object).Handle(new MarkReleaseNotesSeenCommand("x"), default);
        missing.IsSuccess.Should().BeFalse();
        missing.FirstError.Should().Contain("bulunamadı");
    }
}
