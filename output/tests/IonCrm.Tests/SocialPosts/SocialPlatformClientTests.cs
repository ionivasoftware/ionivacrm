using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IonCrm.Application.Common.Models;
using IonCrm.Infrastructure.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IonCrm.Tests.SocialPosts;

/// <summary>Wire-level checks for the Meta Graph and X clients: request shaping, response parsing, error classification.</summary>
public class SocialPlatformClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Auth, string? ContentType)> Calls { get; } = new();
        public Queue<HttpResponseMessage> Responses { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString(), request.Content?.Headers.ContentType?.MediaType));
            return Responses.Count > 0 ? Responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("{}") };
        }
    }

    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Social:Meta:AppId"] = "app-1", ["Social:Meta:AppSecret"] = "sec-1",
        ["Social:X:ClientId"] = "xcid", ["Social:X:ClientSecret"] = "xsecret",
    }).Build();

    private static (MetaGraphClient Client, ScriptedHandler Handler) Meta()
    {
        var h = new ScriptedHandler();
        return (new MetaGraphClient(new HttpClient(h), Config(), NullLogger<MetaGraphClient>.Instance), h);
    }

    private static (XApiClient Client, ScriptedHandler Handler) X()
    {
        var h = new ScriptedHandler();
        return (new XApiClient(new HttpClient(h), Config(), NullLogger<XApiClient>.Instance), h);
    }

    // ── Meta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Meta_facebook_photo_posts_form_fields_and_returns_post_id()
    {
        var (client, h) = Meta();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"id":"111","post_id":"222_333"}""") });

        var id = await client.PublishFacebookPhotoAsync("222", "PAGE-TOKEN", "https://crm/img/1.png", "Metin\n\n#tag");

        id.Should().Be("222_333");
        var call = h.Calls.Single();
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Be("https://graph.facebook.com/v21.0/222/photos");
        call.Body.Should().Contain("url=https%3A%2F%2Fcrm%2Fimg%2F1.png").And.Contain("access_token=PAGE-TOKEN").And.Contain("message=Metin");
    }

    [Fact]
    public async Task Meta_instagram_is_two_steps_with_status_poll()
    {
        var (client, h) = Meta();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"id":"C1"}""") });
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"status_code":"FINISHED","id":"C1"}""") });
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"id":"M9"}""") });

        var id = await client.PublishInstagramImageAsync("IG1", "T", "https://crm/img/2.png", "Caption");

        id.Should().Be("M9");
        h.Calls.Should().HaveCount(3);
        h.Calls[0].Url.Should().EndWith("/IG1/media");
        h.Calls[0].Body.Should().Contain("image_url=").And.Contain("caption=Caption");
        h.Calls[1].Url.Should().StartWith("https://graph.facebook.com/v21.0/C1?fields=status_code");
        h.Calls[2].Url.Should().EndWith("/IG1/media_publish");
        h.Calls[2].Body.Should().Contain("creation_id=C1");
    }

    [Fact]
    public async Task Meta_error_body_becomes_classified_exception()
    {
        var (client, h) = Meta();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json("""{"error":{"message":"Error validating access token: Session has expired","type":"OAuthException","code":190,"error_subcode":463}}"""),
        });

        var act = () => client.PublishFacebookTextAsync("1", "dead", "x");

        var ex = (await act.Should().ThrowAsync<SocialPublishException>()).Which;
        ex.IsPermanent.Should().BeTrue();
        ex.InvalidatesToken.Should().BeTrue();
        ex.ProviderCode.Should().Be("190/463");
        ex.Message.Should().Contain("Session has expired");
    }

    [Theory]
    [InlineData(400, 190, true, true, false)]
    [InlineData(400, 10, true, false, true)]
    [InlineData(403, 200, true, false, true)]
    [InlineData(400, 4, false, false, false)]   // rate limit
    [InlineData(500, 1, false, false, false)]   // unknown/temporary
    [InlineData(400, 100, true, false, false)]  // invalid parameter — block the job, account is fine
    public void Meta_classification_table(int http, int code, bool permanent, bool invalidates, bool accountFault)
    {
        using var doc = JsonDocument.Parse($$$"""{"message":"m","code":{{{code}}}}""");
        var ex = MetaGraphClient.Classify(http, doc.RootElement);
        ex.IsPermanent.Should().Be(permanent);
        ex.InvalidatesToken.Should().Be(invalidates);
        ex.IsAccountFault.Should().Be(accountFault);
    }

    [Fact]
    public async Task Meta_pages_parse_linked_instagram_account()
    {
        var (client, h) = Meta();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"data":[{"id":"P1","name":"Liftdesk","access_token":"PT","instagram_business_account":{"id":"IG1","username":"liftdesk"}},{"id":"P2","name":"Other","access_token":"PT2"}]}"""),
        });

        var pages = await client.GetPagesAsync("USER");

        pages.Should().HaveCount(2);
        pages[0].Should().Be(new MetaPage("P1", "Liftdesk", "PT", "IG1", "liftdesk"));
        pages[1].InstagramId.Should().BeNull();
        h.Calls.Single().Url.Should().Contain("/me/accounts?fields=id,name,access_token,instagram_business_account");
    }

    [Fact]
    public void Meta_authorization_url_carries_scopes_state_and_redirect()
    {
        var (client, _) = Meta();
        var url = client.BuildAuthorizationUrl("https://api/cb", "st4te");
        url.Should().StartWith("https://www.facebook.com/v21.0/dialog/oauth?client_id=app-1")
           .And.Contain("redirect_uri=https%3A%2F%2Fapi%2Fcb").And.Contain("state=st4te")
           .And.Contain("instagram_content_publish").And.Contain("pages_manage_posts");
    }

    [Fact]
    public void Meta_authorization_url_uses_config_id_without_scope_for_business_login()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Social:Meta:AppId"] = "app-1", ["Social:Meta:AppSecret"] = "sec-1", ["Social:Meta:ConfigId"] = "123456789",
        }).Build();
        var client = new MetaGraphClient(new HttpClient(new ScriptedHandler()), config, NullLogger<MetaGraphClient>.Instance);

        var url = client.BuildAuthorizationUrl("https://api/cb", "st4te");

        url.Should().Contain("config_id=123456789").And.Contain("override_default_response_type=true").And.Contain("response_type=code")
           .And.NotContain("scope=");
    }

    // ── X ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task X_code_exchange_sends_pkce_form_with_basic_auth_and_parses_tokens()
    {
        var (client, h) = X();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"token_type":"bearer","expires_in":7200,"access_token":"AT","scope":"tweet.write users.read offline.access","refresh_token":"RT"}"""),
        });

        var t = await client.ExchangeCodeAsync("c0de", "https://api/x/cb", "verifier123");

        t.AccessToken.Should().Be("AT");
        t.RefreshToken.Should().Be("RT");
        t.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(7200), TimeSpan.FromMinutes(1));
        var call = h.Calls.Single();
        call.Url.Should().Be("https://api.x.com/2/oauth2/token");
        call.Body.Should().Contain("grant_type=authorization_code").And.Contain("code_verifier=verifier123").And.Contain("client_id=xcid");
        call.Auth.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("xcid:xsecret")));
    }

    [Fact]
    public async Task X_create_post_sends_text_verbatim_with_media_ids()
    {
        var (client, h) = X();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Created) { Content = Json("""{"data":{"id":"17771","text":"..."}}""") });

        var id = await client.CreatePostAsync("AT", "Kısa metin #etiket", "M1");

        id.Should().Be("17771");
        var call = h.Calls.Single();
        call.Url.Should().Be("https://api.x.com/2/tweets");
        call.Auth.Should().Be("Bearer AT");
        using var body = JsonDocument.Parse(call.Body!);
        body.RootElement.GetProperty("text").GetString().Should().Be("Kısa metin #etiket");
        body.RootElement.GetProperty("media").GetProperty("media_ids")[0].GetString().Should().Be("M1");
    }

    [Fact]
    public async Task X_media_upload_is_multipart_with_category_and_returns_data_id()
    {
        var (client, h) = X();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"data":{"id":"MEDIA-1","media_key":"3_1"}}""") });

        var id = await client.UploadImageAsync("AT", new byte[] { 0x89, 0x50 }, "image/png");

        id.Should().Be("MEDIA-1");
        var call = h.Calls.Single();
        call.Url.Should().Be("https://api.x.com/2/media/upload");
        call.ContentType.Should().Be("multipart/form-data");
        call.Body.Should().Contain("name=media_category").And.Contain("tweet_image").And.Contain("filename=image.png");
    }

    [Theory]
    [InlineData(401, true, true, false)]
    [InlineData(403, true, false, true)]
    [InlineData(429, false, false, false)]
    [InlineData(503, false, false, false)]
    [InlineData(400, true, false, false)]
    public void X_classification_table(int http, bool permanent, bool invalidates, bool accountFault)
    {
        var ex = XApiClient.Classify(http, "detail");
        ex.IsPermanent.Should().Be(permanent);
        ex.InvalidatesToken.Should().Be(invalidates);
        ex.IsAccountFault.Should().Be(accountFault);
    }

    [Fact]
    public async Task X_error_response_surfaces_detail()
    {
        var (client, h) = X();
        h.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = Json("""{"title":"Forbidden","detail":"Your account is not permitted to perform this action.","type":"about:blank","status":403}"""),
        });

        var act = () => client.CreatePostAsync("AT", "x", null);

        var ex = (await act.Should().ThrowAsync<SocialPublishException>()).Which;
        ex.IsAccountFault.Should().BeTrue();
        ex.Message.Should().Contain("not permitted");
    }
}
