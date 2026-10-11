using System.Net;
using System.Text;
using FluentAssertions;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Infrastructure.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IonCrm.Tests.SocialPosts;

/// <summary>Wire-level checks: query building, envelope parsing, status propagation, binary card.</summary>
public class LiftdeskSocialPostsClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond(request);
        }
    }

    private static (LiftdeskSocialPostsClient Client, StubHandler Handler) Build()
    {
        var handler = new StubHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Liftdesk:ApiKey"] = "test-key",
            ["Liftdesk:BaseUrl"] = "https://ems.test",
        }).Build();
        return (new LiftdeskSocialPostsClient(new HttpClient(handler), config, NullLogger<LiftdeskSocialPostsClient>.Instance), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task List_builds_query_sends_bearer_and_parses_page_with_string_enums()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.OK, """
            {"success":true,"data":{"items":[{"id":"6f1c9f1e-2d4b-4a0e-9c1a-0b1e2f3a4b5c","kind":"Holiday","status":"Pending",
              "slotKey":"2026-10-29:holiday:cumhuriyet-bayrami","scheduledFor":"2026-10-29T00:00:00Z","topic":"29 Ekim",
              "holidayName":"Cumhuriyet Bayramı","sourceUrls":null,"customImageUrl":null,"approvedAt":null,"rejectedAt":null,
              "rejectReason":null,"publishedAt":null,"createdAt":"2026-10-25T05:01:00Z","updatedAt":"2026-10-25T05:01:00Z",
              "variants":[{"language":"tr","cardTitle":"Cumhuriyet Bayramı Kutlu Olsun","body":"...","bodyShort":"... #29Ekim","hashtags":"#29Ekim"}]}],
              "totalCount":1,"page":1,"pageSize":20},"message":null,"errors":[],"statusCode":200}
            """);

        var env = await client.ListAsync("Pending", "2026-10-01", null, 1, 20);

        env.Success.Should().BeTrue();
        env.Data!.TotalCount.Should().Be(1);
        env.Data.Items.Should().ContainSingle().Which.Kind.Should().Be("Holiday");
        env.Data.Items![0].Variants.Should().ContainSingle().Which.Language.Should().Be("tr");
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://ems.test/api/v1/crm/social-posts?page=1&pageSize=20&status=Pending&from=2026-10-01");
        handler.LastRequest.Headers.Authorization!.ToString().Should().Be("Bearer test-key");
    }

    [Fact]
    public async Task Conflict_envelope_is_marked_failed_with_409_and_liftdesk_message_kept()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.Conflict,
            """{"success":false,"data":null,"message":null,"errors":["Post not found or not in an approvable state."],"statusCode":409}""");

        var env = await client.ApproveAsync(Guid.NewGuid());

        env.Success.Should().BeFalse();
        env.StatusCode.Should().Be(409);
        env.Errors.Should().ContainSingle().Which.Should().Contain("approvable");
    }

    [Fact]
    public async Task Update_sends_json_body_without_null_fields_and_reject_always_sends_body()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.OK, """{"success":true,"data":null,"statusCode":200}""");

        await client.UpdateAsync(Guid.NewGuid(), new LiftdeskSocialPostUpdate(null, "", null));
        handler.LastRequest!.Method.Should().Be(HttpMethod.Put);
        handler.LastBody.Should().Be("""{"customImageUrl":""}""");

        await client.RejectAsync(Guid.NewGuid(), "   ");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().EndWith("/reject");
        handler.LastBody.Should().Be("{}");

        await client.RejectAsync(Guid.NewGuid(), " weak source ");
        handler.LastBody.Should().Be("""{"reason":"weak source"}""");
    }

    [Fact]
    public async Task MarkPublished_posts_stamp_body_and_empty_object_when_no_time()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.OK, """{"success":true,"data":null,"statusCode":200}""");
        var id = Guid.NewGuid();

        await client.MarkPublishedAsync(id, new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc));
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be($"/api/v1/crm/social-posts/{id}/published");
        handler.LastBody.Should().Be("""{"publishedAt":"2026-10-12T07:00:00Z"}""");

        await client.MarkPublishedAsync(id, null);
        handler.LastBody.Should().Be("{}");
    }

    [Fact]
    public async Task Card_returns_bytes_and_content_type_or_404_envelope()
    {
        var (client, handler) = Build();
        var id = Guid.NewGuid();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0x89, 0x50 }) { Headers = { ContentType = new("image/png") } },
        };

        var ok = await client.GetCardAsync(id, "en-GB");
        ok.Success.Should().BeTrue();
        ok.Data!.ContentType.Should().Be("image/png");
        ok.Data.Content.Should().Equal(0x89, 0x50);
        handler.LastRequest!.RequestUri!.Query.Should().Be("?lang=en-GB");

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var missing = await client.GetCardAsync(id, "tr");
        missing.Success.Should().BeFalse();
        missing.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Unparseable_body_becomes_failed_envelope_with_status()
    {
        var (client, handler) = Build();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") };

        var env = await client.GetAsync(Guid.NewGuid());

        env.Success.Should().BeFalse();
        env.StatusCode.Should().Be(502);
        env.Message.Should().Contain("çözümlenemedi");
    }
}
