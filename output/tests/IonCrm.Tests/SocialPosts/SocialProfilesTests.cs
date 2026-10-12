using System.Net;
using System.Text;
using FluentAssertions;
using IonCrm.API.Controllers;
using IonCrm.Application.Features.SocialPublishing;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IonCrm.Tests.SocialPosts;

public class SocialProfilesTests
{
    [Fact]
    public void Defaults_respect_platform_hard_limits_and_cover_every_key()
    {
        var fb = SocialProfileDefaults.For(SocialProvider.Facebook, "tr");
        var ig = SocialProfileDefaults.For(SocialProvider.Instagram, "tr");

        fb.Keys.Should().BeEquivalentTo(SocialProfileDefaults.FacebookKeys);
        ig.Keys.Should().BeEquivalentTo(SocialProfileDefaults.InstagramKeys);
        fb["about"].Length.Should().BeLessThanOrEqualTo(SocialProfileDefaults.FacebookAboutMax);
        ig["bio"].Length.Should().BeLessThanOrEqualTo(SocialProfileDefaults.InstagramBioMax);
        ig["name"].Length.Should().BeLessThanOrEqualTo(SocialProfileDefaults.InstagramNameMax);
        fb["website"].Should().Be("https://liftdesk.app");
        fb["description"].Should().Contain("QR kodlu");

        // Other languages start empty but with the same keys.
        SocialProfileDefaults.For(SocialProvider.Facebook, "en-GB").Should().HaveCount(SocialProfileDefaults.FacebookKeys.Length)
            .And.OnlyContain(kv => kv.Value == string.Empty);
    }

    [Fact]
    public void Validate_rejects_unknown_keys_and_over_limit_values()
    {
        SocialProfilesController.Validate(SocialProvider.Facebook, new() { ["about"] = new string('a', 256) })
            .Should().Contain("255");
        SocialProfilesController.Validate(SocialProvider.Instagram, new() { ["bio"] = new string('b', 151) })
            .Should().Contain("150");
        SocialProfilesController.Validate(SocialProvider.Instagram, new() { ["about"] = "x" })
            .Should().Contain("Bilinmeyen alan");
        SocialProfilesController.Validate(SocialProvider.Facebook, new() { ["website"] = "liftdesk.app" })
            .Should().Contain("http");
        SocialProfilesController.Validate(SocialProvider.Facebook, new() { ["website"] = "https://liftdesk.app", ["about"] = "ok" })
            .Should().BeNull();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? ContentType)> Calls { get; } = new();
        public string Response { get; set; } = "{\"success\":true}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body, request.Content?.Headers.ContentType?.MediaType));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }

    private static (MetaGraphClient Client, ScriptedHandler Handler) Meta()
    {
        var h = new ScriptedHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Social:Meta:AppId"] = "a", ["Social:Meta:AppSecret"] = "s" }).Build();
        return (new MetaGraphClient(new HttpClient(h), config, NullLogger<MetaGraphClient>.Instance), h);
    }

    [Fact]
    public async Task UpdatePage_sends_only_pushable_non_empty_fields_as_json_with_emails_list()
    {
        var (client, h) = Meta();

        await client.UpdatePageAsync("P1", "TOK", new Dictionary<string, string>
        {
            ["about"] = " Hakkında ", ["description"] = "Uzun", ["website"] = "https://liftdesk.app", ["phone"] = "", ["email"] = "info@liftdesk.app",
            ["name"] = "LiftDesk", // not pushable — must be ignored
        });

        var call = h.Calls.Single();
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Be("https://graph.facebook.com/v21.0/P1");
        call.ContentType.Should().Be("application/json");
        using var doc = System.Text.Json.JsonDocument.Parse(call.Body!);
        var root = doc.RootElement;
        root.GetProperty("about").GetString().Should().Be("Hakkında");
        root.GetProperty("emails")[0].GetString().Should().Be("info@liftdesk.app");
        root.GetProperty("access_token").GetString().Should().Be("TOK");
        root.TryGetProperty("phone", out _).Should().BeFalse("empty values are skipped");
        root.TryGetProperty("name", out _).Should().BeFalse("name is not writable through the Pages API");
    }

    [Fact]
    public async Task UpdatePage_with_nothing_to_send_makes_no_request()
    {
        var (client, h) = Meta();
        await client.UpdatePageAsync("P1", "TOK", new Dictionary<string, string> { ["phone"] = "  ", ["name"] = "x" });
        h.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetPagePicture_uploads_multipart_source()
    {
        var (client, h) = Meta();
        h.Response = "{\"id\":\"PH1\"}";

        var id = await client.SetPagePictureAsync("P1", "TOK", new byte[] { 0x89, 0x50 }, "image/png");

        id.Should().Be("PH1");
        var call = h.Calls.Single();
        call.Url.Should().Be("https://graph.facebook.com/v21.0/P1/picture");
        call.ContentType.Should().Be("multipart/form-data");
        call.Body.Should().Contain("name=source").And.Contain("filename=profile.png").And.Contain("name=access_token");
    }
}
