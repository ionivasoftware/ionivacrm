using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Application.Features.Parasut;
using IonCrm.Application.Features.Parasut.Commands.CompleteParasutOAuth;
using IonCrm.Application.Features.Parasut.Commands.DisconnectParasut;
using IonCrm.Application.Features.Parasut.Commands.StartParasutOAuth;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.ExternalApis;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IonCrm.Tests.Parasut;

/// <summary>
/// Paraşüt's NEW authorisation model (04–05.11.2026 announcement): applications lose access to the user's password; connecting
/// happens on Paraşüt's own login screen with authorization code + PKCE. Locked here: the shape of the authorization URL
/// (S256, scope=openid, staging host), the encrypted verifier and the challenge derived from it, the SINGLE-USE state,
/// no password fallback when a refresh is rejected, revocation on disconnect, and legacy plain-text rows that keep working.
/// </summary>
public class ParasutOAuthFlowTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static ApplicationDbContext Db()
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(x => x.IsSuperAdmin).Returns(true);
        current.Setup(x => x.ProjectIds).Returns(new List<Guid>());
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, current.Object);
    }

    private static AesGcmTokenProtector Protector() => new("test-key-for-parasut-oauth");

    private static ICurrentUserService CurrentUser()
    {
        var m = new Mock<ICurrentUserService>();
        m.Setup(x => x.UserId).Returns(UserId);
        m.Setup(x => x.IsSuperAdmin).Returns(true);
        return m.Object;
    }

    private static ParasutOAuthSettings Staging(string clientId = "ioncrm", string? secret = "gizli", string redirect = "https://api.test/api/v1/parasut/callback") =>
        new(key => key switch
        {
            "Parasut:OAuth:Environment" => "Staging",
            "Parasut:OAuth:ClientId" => clientId,
            "Parasut:OAuth:ClientSecret" => secret,
            "Parasut:OAuth:RedirectUri" => redirect,
            _ => null,
        });

    /// <summary>Fake client that captures the state + challenge handed to the authorization URL.</summary>
    private static Mock<IParasutClient> StartingClient(Action<string, string>? capture = null)
    {
        var client = new Mock<IParasutClient>();
        client.SetupGet(c => c.OAuth2Available).Returns(true);
        client.Setup(c => c.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string state, string challenge) => { capture?.Invoke(state, challenge); return $"https://id-staging.parasut.com/auth?state={state}&code_challenge={challenge}"; });
        return client;
    }

    private static ParasutTokenResponse Tokens(string access = "yeni-access", string refresh = "yeni-refresh", int refreshExpiresIn = 2_592_000) =>
        new(access, "Bearer", 7200, refresh, 0, refreshExpiresIn);

    private static StartParasutOAuthCommandHandler StartHandler(ApplicationDbContext db, IParasutClient client) =>
        new(client, new ParasutConnectionRepository(db), Protector(), CurrentUser(), NullLogger<StartParasutOAuthCommandHandler>.Instance);

    private static CompleteParasutOAuthCommandHandler CompleteHandler(ApplicationDbContext db, IParasutClient client) =>
        new(client, new ParasutConnectionRepository(db), Protector(), NullLogger<CompleteParasutOAuthCommandHandler>.Instance);

    private static DisconnectParasutCommandHandler DisconnectHandler(ApplicationDbContext db, IParasutClient client) =>
        new(new ParasutConnectionRepository(db), client, Protector(), NullLogger<DisconnectParasutCommandHandler>.Instance);

    private static Task<(ParasutConnection? Connection, string? Error)> Ensure(ApplicationDbContext db, ParasutConnection row, IParasutClient client) =>
        ParasutTokenHelper.EnsureValidTokenAsync(row, client, new ParasutConnectionRepository(db), Protector(), NullLogger.Instance, CancellationToken.None);

    // ── Authorization URL (real client) ──────────────────────────────────────

    [Fact]
    public void Authorization_url_carries_pkce_scope_and_the_registered_redirect()
    {
        var client = new ParasutClient(new HttpClient(), Staging(), NullLogger<ParasutClient>.Instance);

        var url = client.BuildAuthorizationUrl("DURUM", "MEYDAN");

        url.Should().StartWith("https://id-staging.parasut.com/realms/parasut/protocol/openid-connect/auth?");
        url.Should().Contain("client_id=ioncrm").And.Contain("response_type=code").And.Contain("scope=openid").And.Contain("state=DURUM")
            .And.Contain("code_challenge=MEYDAN").And.Contain("code_challenge_method=S256").And.Contain("kc_idp_hint=parasut")
            .And.Contain("redirect_uri=https%3A%2F%2Fapi.test%2Fapi%2Fv1%2Fparasut%2Fcallback");
        url.Should().NotContain("api.parasut.com/oauth", "the new flow lives on Keycloak");
        url.Should().NotContain("gizli", "the secret never travels in the browser URL");
    }

    [Fact]
    public void Production_environment_uses_production_identity_and_rest_hosts()
    {
        var settings = new ParasutOAuthSettings(key => key switch
        {
            "Parasut:OAuth:ClientId" => "ioncrm",
            "Parasut:OAuth:RedirectUri" => "https://ion-crm-api-production.up.railway.app/api/v1/parasut/callback",
            _ => null,
        });

        settings.IsStaging.Should().BeFalse();
        settings.AuthorizationEndpoint.Should().Be("https://id.parasut.com/realms/parasut/protocol/openid-connect/auth");
        settings.TokenEndpoint.Should().Be("https://id.parasut.com/realms/parasut/protocol/openid-connect/token");
        settings.RevocationEndpoint.Should().Be("https://id.parasut.com/realms/parasut/protocol/openid-connect/revoke");
        settings.ApiBaseUrl.Should().Be("https://api.parasut.com");
        settings.Available.Should().BeTrue("a public client has no secret");
        settings.ClientSecret.Should().BeNull();
        Staging().ApiBaseUrl.Should().Be("https://api.heroku-staging.parasut.com");
        new ParasutOAuthSettings(_ => null).Available.Should().BeFalse();
    }

    // ── Start ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_stores_hashed_state_and_encrypted_verifier_and_derives_the_challenge()
    {
        await using var db = Db();
        string? state = null, challenge = null;
        var client = StartingClient((s, c) => { state = s; challenge = c; });

        var result = await StartHandler(db, client.Object).Handle(new StartParasutOAuthCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AuthorizationUrl.Should().Contain(state!);
        var row = await db.ParasutConnections.IgnoreQueryFilters().SingleAsync();
        row.ProjectId.Should().BeNull("only the global connection uses the new flow");
        row.OAuthStateUserId.Should().Be(UserId);
        row.OAuthStateExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromMinutes(1));
        row.OAuthStateHash.Should().NotBeNullOrEmpty().And.NotBe(state).And.HaveLength(64);
        row.OAuthStateHash.Should().Be(ParasutOAuthFlow.HashState(state!));

        // Verifier encrypted at rest; the challenge is BASE64URL(SHA256(verifier)).
        row.OAuthCodeVerifier.Should().NotBeNullOrEmpty();
        var verifier = Protector().Unprotect(row.OAuthCodeVerifier!);
        row.OAuthCodeVerifier.Should().NotContain(verifier);
        verifier.Length.Should().BeInRange(43, 128, "RFC 7636");
        challenge.Should().Be(ParasutOAuthFlow.CodeChallenge(verifier));
    }

    [Fact]
    public async Task Start_fails_when_the_new_flow_is_not_configured()
    {
        await using var db = Db();
        var client = new Mock<IParasutClient>();
        client.SetupGet(c => c.OAuth2Available).Returns(false);

        var result = await StartHandler(db, client.Object).Handle(new StartParasutOAuthCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Single().Should().Contain("Parasut__OAuth__ClientId");
        (await db.ParasutConnections.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    // ── Callback ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_success_stores_tokens_company_and_user_and_drops_the_password()
    {
        await using var db = Db();
        // A legacy row with a stored password is upgraded in place.
        db.ParasutConnections.Add(new ParasutConnection { ProjectId = null, CompanyId = 0, Username = "muhasebe@ioniva.com", Password = "eski-parola", ClientId = "old", ClientSecret = "old" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        string? state = null;
        var client = StartingClient((s, _) => state = s);
        client.Setup(c => c.ExchangeCodeAsync("kod", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tokens());
        client.Setup(c => c.GetMeAsync("yeni-access", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParasutMeResponse(new[] { new ParasutMeCompany(98765, "IONIVA Yazılım") }));
        await StartHandler(db, client.Object).Handle(new StartParasutOAuthCommand(), CancellationToken.None);

        var result = await CompleteHandler(db, client.Object).Handle(new CompleteParasutOAuthCommand("kod", state, null), CancellationToken.None);

        result.IsConnected.Should().BeTrue(result.Reason);
        var row = await db.ParasutConnections.IgnoreQueryFilters().SingleAsync();
        row.AuthFlow.Should().Be(ParasutAuthFlow.OAuth2);
        row.CompanyId.Should().Be(98765);
        row.CompanyName.Should().Be("IONIVA Yazılım");
        row.AccessToken.Should().Be("yeni-access", "the access token stays plain — nine call sites read it");
        row.RefreshToken.Should().NotBeNullOrEmpty().And.NotContain("yeni-refresh", "the refresh token is encrypted at rest");
        Protector().Unprotect(row.RefreshToken!).Should().Be("yeni-refresh");
        row.RefreshTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        row.TokenExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(90));
        row.NeedsReconnect.Should().BeFalse();
        row.ConnectedAt.Should().NotBeNull();
        row.ConnectedByUserId.Should().Be(UserId);
        row.Password.Should().BeEmpty("the new flow never keeps a password");
        row.Username.Should().Be("muhasebe@ioniva.com", "informative, kept");
        row.IsDeleted.Should().BeFalse();
        // Single use: state and verifier are gone.
        row.OAuthStateHash.Should().BeNull();
        row.OAuthCodeVerifier.Should().BeNull();
        row.OAuthStateUserId.Should().BeNull();
    }

    [Fact]
    public async Task Callback_keeps_an_existing_company_when_me_still_lists_it()
    {
        await using var db = Db();
        db.ParasutConnections.Add(new ParasutConnection { ProjectId = null, CompanyId = 222 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        string? state = null;
        var client = StartingClient((s, _) => state = s);
        client.Setup(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tokens());
        client.Setup(c => c.GetMeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParasutMeResponse(new[] { new ParasutMeCompany(111, "Diğer"), new ParasutMeCompany(222, "Bizim") }));
        await StartHandler(db, client.Object).Handle(new StartParasutOAuthCommand(), CancellationToken.None);

        (await CompleteHandler(db, client.Object).Handle(new CompleteParasutOAuthCommand("kod", state, null), CancellationToken.None)).IsConnected.Should().BeTrue();

        var row = await db.ParasutConnections.IgnoreQueryFilters().SingleAsync();
        row.CompanyId.Should().Be(222);
        row.CompanyName.Should().Be("Bizim");
    }

    [Fact]
    public async Task Callback_rejects_a_replayed_state()
    {
        await using var db = Db();
        string? state = null;
        var client = StartingClient((s, _) => state = s);
        client.Setup(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tokens());
        client.Setup(c => c.GetMeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ParasutMeResponse(new[] { new ParasutMeCompany(98765, "X") }));
        await StartHandler(db, client.Object).Handle(new StartParasutOAuthCommand(), CancellationToken.None);
        var handler = CompleteHandler(db, client.Object);

        (await handler.Handle(new CompleteParasutOAuthCommand("kod", state, null), CancellationToken.None)).IsConnected.Should().BeTrue();
        var again = await handler.Handle(new CompleteParasutOAuthCommand("kod", state, null), CancellationToken.None);

        again.Outcome.Should().Be("error");
        again.Reason.Should().Be("state", "state is single use — a replay is refused");
        client.Verify(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Callback_rejects_an_unknown_state()
    {
        await using var db = Db();
        var client = StartingClient();

        var result = await CompleteHandler(db, client.Object).Handle(new CompleteParasutOAuthCommand("kod", "uydurma", null), CancellationToken.None);

        result.Outcome.Should().Be("error");
        result.Reason.Should().Be("state");
        client.Verify(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Callback_rejects_an_expired_state_without_exchanging_the_code()
    {
        await using var db = Db();
        string? state = null;
        var client = StartingClient((s, _) => state = s);
        await StartHandler(db, client.Object).Handle(new StartParasutOAuthCommand(), CancellationToken.None);
        var row = await db.ParasutConnections.IgnoreQueryFilters().SingleAsync();
        row.OAuthStateExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CompleteHandler(db, client.Object).Handle(new CompleteParasutOAuthCommand("kod", state, null), CancellationToken.None);

        result.Reason.Should().Be("expired");
        client.Verify(c => c.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        (await db.ParasutConnections.IgnoreQueryFilters().SingleAsync()).OAuthStateHash.Should().BeNull("cleared even on failure");
    }

    [Fact]
    public async Task Callback_denied_by_the_user_leaves_the_legacy_row_intact()
    {
        await using var db = Db();
        db.ParasutConnections.Add(new ParasutConnection { ProjectId = null, CompanyId = 5, Username = "u", Password = "p", AccessToken = "eski", RefreshToken = "eski-refresh", TokenExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        string? state = null;
        var client = StartingClient((s, _) => state = s);
        await StartHandler(db, client.Object).Handle(new StartParasutOAuthCommand(), CancellationToken.None);

        var result = await CompleteHandler(db, client.Object).Handle(new CompleteParasutOAuthCommand(null, state, "access_denied"), CancellationToken.None);

        result.Reason.Should().Be("denied");
        var row = await db.ParasutConnections.IgnoreQueryFilters().SingleAsync();
        row.AuthFlow.Should().Be(ParasutAuthFlow.Password);
        row.Password.Should().Be("p");
        row.AccessToken.Should().Be("eski");
        row.RefreshToken.Should().Be("eski-refresh");
        row.OAuthStateHash.Should().BeNull();
    }

    // ── Refresh: no password fallback on the new flow ────────────────────────

    [Fact]
    public async Task Rejected_refresh_marks_needs_reconnect_and_never_tries_the_password()
    {
        await using var db = Db();
        var protector = Protector();
        var row = new ParasutConnection
        {
            ProjectId = null, CompanyId = 98765, AuthFlow = ParasutAuthFlow.OAuth2,
            AccessToken = "suresi-dolmus", RefreshToken = protector.Protect("dusmus-refresh"), TokenExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            // A leftover password from the legacy flow must NOT be used.
            Username = "firma@ornek.com", Password = "eski-parola",
        };
        db.ParasutConnections.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var client = new Mock<IParasutClient>();
        client.Setup(c => c.RefreshOAuth2TokenAsync("dusmus-refresh", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ParasutTokenRejectedException(400, "invalid_grant"));

        var (conn, error) = await Ensure(db, row, client.Object);

        conn.Should().BeNull();
        error.Should().Be(ParasutTokenHelper.ReconnectRequiredMessage);
        client.Verify(c => c.GetTokenAsync(It.IsAny<ParasutTokenRequest>(), It.IsAny<CancellationToken>()), Times.Never, "there is no password fallback on the new flow");
        client.Verify(c => c.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var saved = await db.ParasutConnections.IgnoreQueryFilters().SingleAsync();
        saved.NeedsReconnect.Should().BeTrue();
        saved.LastError.Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Transient_refresh_failure_does_not_kill_the_connection()
    {
        await using var db = Db();
        var row = new ParasutConnection { ProjectId = null, CompanyId = 1, AuthFlow = ParasutAuthFlow.OAuth2, AccessToken = "x", RefreshToken = Protector().Protect("r"), TokenExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        db.ParasutConnections.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var client = new Mock<IParasutClient>();
        client.Setup(c => c.RefreshOAuth2TokenAsync("r", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("timeout"));

        var (conn, error) = await Ensure(db, row, client.Object);

        conn.Should().BeNull();
        error.Should().Be(ParasutTokenHelper.TemporarilyUnavailableMessage);
        row.NeedsReconnect.Should().BeFalse("a network error is not a verdict on the token");
    }

    [Fact]
    public async Task Flagged_connection_makes_no_call_at_all()
    {
        await using var db = Db();
        var row = new ParasutConnection { ProjectId = null, CompanyId = 1, AuthFlow = ParasutAuthFlow.OAuth2, NeedsReconnect = true, AccessToken = "gecerli-gorunen", RefreshToken = "r", TokenExpiresAt = DateTime.UtcNow.AddHours(1) };
        var client = new Mock<IParasutClient>(MockBehavior.Strict);

        var (conn, error) = await Ensure(db, row, client.Object);

        conn.Should().BeNull();
        error.Should().Be(ParasutTokenHelper.ReconnectRequiredMessage);
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Successful_refresh_rotates_the_refresh_token_and_renews_the_window()
    {
        await using var db = Db();
        var protector = Protector();
        var row = new ParasutConnection { ProjectId = null, CompanyId = 1, AuthFlow = ParasutAuthFlow.OAuth2, AccessToken = "eski", RefreshToken = protector.Protect("eski-refresh"), TokenExpiresAt = DateTime.UtcNow.AddMinutes(-5), RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(3), ReconnectAttempts = 4, LastError = "x", ReconnectAlertedAt = DateTime.UtcNow };
        db.ParasutConnections.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var client = new Mock<IParasutClient>();
        client.Setup(c => c.RefreshOAuth2TokenAsync("eski-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(Tokens("taze", "taze-refresh"));

        var (conn, error) = await Ensure(db, row, client.Object);

        error.Should().BeNull();
        conn!.AccessToken.Should().Be("taze");
        protector.Unprotect(conn.RefreshToken!).Should().Be("taze-refresh", "rotation: the new refresh token replaces the old one");
        conn.RefreshTokenExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        conn.ReconnectAttempts.Should().Be(0);
        conn.LastError.Should().BeNull();
        conn.ReconnectAlertedAt.Should().BeNull("a recovered connection may alert again next time");
    }

    // ── Legacy rows ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Legacy_plain_refresh_token_is_read_as_is_and_encrypted_after_the_refresh()
    {
        await using var db = Db();
        var row = new ParasutConnection { ProjectId = null, CompanyId = 1, AuthFlow = ParasutAuthFlow.Password, ClientId = "cid", ClientSecret = "sec", Username = "u", Password = "p", AccessToken = "eski", RefreshToken = "duz-metin-refresh", TokenExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        db.ParasutConnections.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var client = new Mock<IParasutClient>();
        client.Setup(c => c.RefreshTokenAsync("duz-metin-refresh", "cid", "sec", It.IsAny<CancellationToken>())).ReturnsAsync(Tokens("tazelendi", "tazelenmis-refresh", refreshExpiresIn: 0));

        var (conn, error) = await Ensure(db, row, client.Object);

        error.Should().BeNull();
        client.Verify(c => c.RefreshTokenAsync("duz-metin-refresh", "cid", "sec", It.IsAny<CancellationToken>()), Times.Once, "the plain value is used as stored");
        conn!.RefreshToken.Should().NotBe("tazelenmis-refresh");
        Protector().Unprotect(conn.RefreshToken!).Should().Be("tazelenmis-refresh", "written encrypted from now on");
        conn.RefreshTokenExpiresAt.Should().BeNull("the legacy endpoint reports no refresh lifetime");
        client.Verify(c => c.RefreshOAuth2TokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Legacy_row_still_falls_back_to_the_password_grant()
    {
        await using var db = Db();
        var row = new ParasutConnection { ProjectId = null, CompanyId = 1, AuthFlow = ParasutAuthFlow.Password, ClientId = "cid", ClientSecret = "sec", Username = "u", Password = "p", AccessToken = "eski", RefreshToken = "r", TokenExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        db.ParasutConnections.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var client = new Mock<IParasutClient>();
        client.Setup(c => c.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("400"));
        client.Setup(c => c.GetTokenAsync(It.Is<ParasutTokenRequest>(r => r.GrantType == "password" && r.Username == "u" && r.Password == "p"), It.IsAny<CancellationToken>())).ReturnsAsync(Tokens("parola-ile", "pr", 0));

        var (conn, error) = await Ensure(db, row, client.Object);

        error.Should().BeNull();
        conn!.AccessToken.Should().Be("parola-ile");
        conn.NeedsReconnect.Should().BeFalse();
    }

    // ── Disconnect ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Disconnect_on_the_new_flow_revokes_the_plain_refresh_token_at_parasut()
    {
        await using var db = Db();
        var protector = Protector();
        db.ParasutConnections.Add(new ParasutConnection { ProjectId = null, CompanyId = 1, AuthFlow = ParasutAuthFlow.OAuth2, AccessToken = "a", RefreshToken = protector.Protect("canli-refresh"), TokenExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var client = new Mock<IParasutClient>();
        client.Setup(c => c.RevokeOAuth2TokenAsync("canli-refresh", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await DisconnectHandler(db, client.Object).Handle(new DisconnectParasutCommand(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        client.Verify(c => c.RevokeOAuth2TokenAsync("canli-refresh", It.IsAny<CancellationToken>()), Times.Once, "revocation gets the PLAIN token");
        var row = await db.ParasutConnections.IgnoreQueryFilters().SingleAsync();
        row.IsDeleted.Should().BeTrue();
        row.RefreshToken.Should().BeNull("tokens never outlive the connection");
        row.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task Disconnect_on_the_legacy_flow_does_not_call_revoke()
    {
        await using var db = Db();
        db.ParasutConnections.Add(new ParasutConnection { ProjectId = null, CompanyId = 1, AuthFlow = ParasutAuthFlow.Password, AccessToken = "a", RefreshToken = "duz", TokenExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var client = new Mock<IParasutClient>();

        (await DisconnectHandler(db, client.Object).Handle(new DisconnectParasutCommand(null), CancellationToken.None)).IsSuccess.Should().BeTrue();

        client.Verify(c => c.RevokeOAuth2TokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "that token does not live at Keycloak");
    }

    // ── Pure helpers ─────────────────────────────────────────────────────────

    [Fact]
    public void Secrets_round_trip_and_plain_values_fall_through()
    {
        var protector = Protector();
        var stored = ParasutOAuthFlow.ProtectSecret(protector, "gizli-deger");
        stored.Should().NotBe("gizli-deger");
        ParasutOAuthFlow.ReadSecret(protector, stored).Should().Be("gizli-deger");
        ParasutOAuthFlow.ReadSecret(protector, "duz-metin-refresh-token").Should().Be("duz-metin-refresh-token");
        ParasutOAuthFlow.ReadSecret(protector, Convert.ToBase64String(new byte[64])).Should().Be(Convert.ToBase64String(new byte[64]), "valid base64 that is not our ciphertext is still returned as is");
        ParasutOAuthFlow.ReadSecret(protector, null).Should().BeEmpty();
        ParasutOAuthFlow.ProtectSecret(protector, null).Should().BeNull();
        ParasutOAuthFlow.NewCodeVerifier().Length.Should().Be(86);
        ParasutOAuthFlow.NewState().Should().NotBe(ParasutOAuthFlow.NewState());
    }

    [Fact]
    public void Token_response_reads_refresh_expires_in_and_tolerates_its_absence()
    {
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };
        var keycloak = System.Text.Json.JsonSerializer.Deserialize<ParasutTokenResponse>("{\"access_token\":\"a\",\"expires_in\":7200,\"refresh_expires_in\":2592000,\"refresh_token\":\"r\",\"token_type\":\"Bearer\"}", opts)!;
        keycloak.RefreshExpiresIn.Should().Be(2_592_000);
        keycloak.CreatedAt.Should().Be(0);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<ParasutTokenResponse>("{\"access_token\":\"a\",\"token_type\":\"Bearer\",\"expires_in\":7200,\"refresh_token\":\"r\",\"created_at\":1700000000}", opts)!;
        legacy.RefreshExpiresIn.Should().Be(0);
        legacy.CreatedAt.Should().Be(1700000000);
    }
}
