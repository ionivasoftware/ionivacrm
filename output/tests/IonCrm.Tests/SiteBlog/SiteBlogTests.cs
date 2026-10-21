using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IonCrm.API.Common;
using IonCrm.API.Controllers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Infrastructure.ExternalApis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace IonCrm.Tests.SiteBlog;

/// <summary>Wire-level checks for the Liftdesk site-blog client: paths, bearer, envelope incl. errorCode/errors, status propagation.</summary>
public class LiftdeskSiteBlogClientTests
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

    private static (LiftdeskSiteBlogClient Client, StubHandler Handler) Build()
    {
        var handler = new StubHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Liftdesk:ApiKey"] = "test-key",
            ["Liftdesk:BaseUrl"] = "https://ems.test",
        }).Build();
        return (new LiftdeskSiteBlogClient(new HttpClient(handler), config, NullLogger<LiftdeskSiteBlogClient>.Instance), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task List_builds_query_and_parses_posts_with_string_enums_and_date_only_fields()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.OK, """
            {"success":true,"data":{"items":[{"id":"6f1c9f1e-2d4b-4a0e-9c1a-0b1e2f3a4b5c","group":"contract-renewal-calendar",
              "topic":"Bakım sözleşmesi yenileme takvimi","source":"Rotation","topicRequestId":null,"sourceUrls":["https://a"],
              "createdAt":"2026-10-20T06:01:00Z","overallStatus":"Pending",
              "variants":[{"id":"7f1c9f1e-2d4b-4a0e-9c1a-0b1e2f3a4b5d","locale":"ie","slug":"lift-maintenance-contract-renewal",
                "url":"https://liftdesk.app/ie/blog/lift-maintenance-contract-renewal/","title":"T","description":"D","bodyMarkdown":null,
                "relatedPageSlug":"contracts","publishDate":"2026-10-27","updatedDate":null,"words":640,"status":"Pending",
                "approvedAt":null,"rejectedAt":null,"rejectReason":null,"publishedAt":null,"liveAt":null,"updatedAt":"2026-10-20T06:01:00Z"}]}],
              "totalCount":1,"page":1,"pageSize":20},"message":null,"errors":[],"statusCode":200}
            """);

        var env = await client.ListAsync("Pending", "ie", 1, 20);

        env.Success.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://ems.test/api/v1/crm/site-blog?page=1&pageSize=20&status=Pending&locale=ie");
        handler.LastRequest.Headers.Authorization!.ToString().Should().Be("Bearer test-key");
        var post = env.Data!.Items.Should().ContainSingle().Subject;
        post.Source.Should().Be("Rotation"); post.OverallStatus.Should().Be("Pending");
        var v = post.Variants.Should().ContainSingle().Subject;
        v.PublishDate.Should().Be("2026-10-27"); v.BodyMarkdown.Should().BeNull(); v.Words.Should().Be(640);
    }

    [Fact]
    public async Task Validation_failure_keeps_errors_and_errorCode_with_400()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.BadRequest, """
            {"success":false,"data":null,"message":"Validation failed","errors":["ie:description_length: 120-160 characters","ie:faq_count: exactly 3 questions"],"statusCode":400,"errorCode":"SITE_BLOG_VALIDATION"}
            """);

        var env = await client.UpdateVariantAsync(Guid.NewGuid(), new LiftdeskSiteBlogVariantUpdate("T", null, null, null, null, null));

        env.Success.Should().BeFalse(); env.StatusCode.Should().Be(400);
        env.ErrorCode.Should().Be("SITE_BLOG_VALIDATION");
        env.Errors.Should().HaveCount(2);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Put);
        var sent = JsonSerializer.Deserialize<JsonElement>(handler.LastBody!);
        sent.GetProperty("title").GetString().Should().Be("T");
        sent.TryGetProperty("slug", out _).Should().BeFalse("null fields are not sent (untouched)");
    }

    [Fact]
    public async Task Bare_404_without_body_becomes_turkish_envelope()
    {
        var (client, handler) = Build();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var env = await client.GetAsync(Guid.NewGuid());
        env.Success.Should().BeFalse(); env.StatusCode.Should().Be(404); env.Message.Should().Contain("bulunamadı");
    }

    [Fact]
    public async Task Reject_sends_json_body_even_without_reason_and_topic_request_posts_fields()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.OK, """{"success":true,"data":{"id":"6f1c9f1e-2d4b-4a0e-9c1a-0b1e2f3a4b5c","topic":"x","group":null,"note":null,"status":"Open","takenByPostId":null,"createdAt":"2026-10-21T00:00:00Z"},"errors":[],"statusCode":200}""");

        await client.RejectVariantAsync(Guid.NewGuid(), "  ");
        handler.LastBody.Should().Be("{}");

        var env = await client.CreateTopicRequestAsync("QR etiketi yıpranınca", "qr-labels", null);
        env.Data!.Status.Should().Be("Open");
        var sent = JsonSerializer.Deserialize<JsonElement>(handler.LastBody!);
        sent.GetProperty("group").GetString().Should().Be("qr-labels");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/crm/site-blog/topic-requests");
    }

    [Fact]
    public async Task Publish_result_reason_is_read()
    {
        var (client, handler) = Build();
        handler.Respond = _ => Json(HttpStatusCode.OK, """{"success":true,"data":{"triggered":false,"reason":"not-configured","awaitingPublish":2,"triggeredAt":null},"errors":[],"statusCode":200}""");
        var env = await client.PublishAsync();
        env.Data!.Triggered.Should().BeFalse(); env.Data.Reason.Should().Be("not-configured"); env.Data.AwaitingPublish.Should().Be(2);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
    }
}

/// <summary>Controller: Turkish pre-checks, validator errors passed through with the errorCode, status mapping.</summary>
public class SiteBlogControllerTests
{
    private readonly Mock<ILiftdeskSiteBlogClient> _client = new();

    private SiteBlogController Build(bool configured = true)
    {
        _client.SetupGet(c => c.IsConfigured).Returns(configured);
        return new SiteBlogController(_client.Object, NullLogger<SiteBlogController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static LiftdeskSiteBlogPost Post(string status = "Pending") => new(
        Guid.NewGuid(), "g", "Konu", "Rotation", null, new List<string>(), DateTime.UtcNow, status,
        new List<LiftdeskSiteBlogVariant> { new(Guid.NewGuid(), "tr", "slug", "https://liftdesk.app/blog/slug/", "T", new string('a', 130), "## A\n\ntext", null, "2026-10-27", null, 500, status, null, null, null, null, null, DateTime.UtcNow) });

    private static int StatusOf(IActionResult result) => (result as ObjectResult)?.StatusCode ?? 0;
    private static ApiResponse<object> BodyOf(IActionResult result) => (ApiResponse<object>)((ObjectResult)result).Value!;

    [Theory]
    [InlineData("", null, null, null, "Başlık")]
    [InlineData(null, "kısa", null, null, "Açıklama")]
    [InlineData(null, null, "Bad Slug", null, "Slug")]
    [InlineData(null, null, null, "27.10.2026", "Yayın tarihi")]
    public void ValidateUpdate_rejects_bad_fields_in_turkish(string? title, string? description, string? slug, string? date, string expectedWord)
    {
        var err = SiteBlogController.ValidateUpdate(new UpdateSiteBlogVariantBody(title, description, slug, null, null, date));
        err.Should().Contain(expectedWord);
    }

    [Fact]
    public void ValidateUpdate_accepts_partial_body()
    {
        SiteBlogController.ValidateUpdate(new UpdateSiteBlogVariantBody(null, new string('a', 140), "lift-maintenance", "## A\n\nb", "", "2026-10-27")).Should().BeNull();
    }

    [Fact]
    public async Task Update_passes_validator_errors_and_errorCode_through()
    {
        _client.Setup(c => c.UpdateVariantAsync(It.IsAny<Guid>(), It.IsAny<LiftdeskSiteBlogVariantUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskEnvelope<LiftdeskSiteBlogPost>(false, null, "Validation failed", new List<string> { "tr:word_count: 450-1000 words" }, 400, "SITE_BLOG_VALIDATION"));
        var result = await Build().UpdateVariant(Guid.NewGuid(), new UpdateSiteBlogVariantBody(null, null, null, "## A\n\nkısa", null, null));
        StatusOf(result).Should().Be(400);
        var body = BodyOf(result);
        body.Errors.Should().ContainSingle().Which.Should().StartWith("tr:word_count");
        body.Message.Should().Be("SITE_BLOG_VALIDATION");
    }

    [Fact]
    public async Task Update_slug_locked_maps_to_turkish_400()
    {
        _client.Setup(c => c.UpdateVariantAsync(It.IsAny<Guid>(), It.IsAny<LiftdeskSiteBlogVariantUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskEnvelope<LiftdeskSiteBlogPost>(false, null, "Slug is locked", null, 400, "SITE_BLOG_SLUG_LOCKED"));
        var result = await Build().UpdateVariant(Guid.NewGuid(), new UpdateSiteBlogVariantBody(null, null, "new-slug", null, null, null));
        StatusOf(result).Should().Be(400);
        BodyOf(result).Errors[0].Should().Contain("slug");
        BodyOf(result).Message.Should().Be("SITE_BLOG_SLUG_LOCKED");
    }

    [Fact]
    public async Task Reject_on_published_maps_409_to_retire_hint()
    {
        _client.Setup(c => c.RejectVariantAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskEnvelope<LiftdeskSiteBlogPost>(false, null, "Invalid state", null, 409, "SITE_BLOG_STATE"));
        var result = await Build().Reject(Guid.NewGuid(), new RejectSiteBlogBody(null));
        StatusOf(result).Should().Be(409);
        BodyOf(result).Errors[0].Should().Contain("Yayından düşür");
    }

    [Fact]
    public async Task Delete_with_published_variant_is_409_and_success_returns_deleted_flag()
    {
        _client.Setup(c => c.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskEnvelope<object>(false, null, "conflict", null, 409, "SITE_BLOG_STATE"));
        StatusOf(await Build().Delete(Guid.NewGuid())).Should().Be(409);

        _client.Setup(c => c.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskEnvelope<object>(true, new { deleted = true }, null, null, 200));
        StatusOf(await Build().Delete(Guid.NewGuid())).Should().Be(200);
    }

    [Fact]
    public async Task Pending_count_is_zero_when_not_configured_and_list_total_otherwise()
    {
        var zero = await Build(configured: false).PendingCount();
        JsonSerializer.Serialize(((ObjectResult)zero).Value).Should().Contain("\"count\":0");

        _client.Setup(c => c.ListAsync("Pending", null, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiftdeskEnvelope<LiftdeskSiteBlogPage>(true, new LiftdeskSiteBlogPage(new List<LiftdeskSiteBlogPost> { Post() }, 3, 1, 1), null, null, 200));
        var result = await Build().PendingCount();
        JsonSerializer.Serialize(((ObjectResult)result).Value).Should().Contain("\"count\":3");
    }

    [Fact]
    public async Task List_rejects_unknown_status_before_calling_liftdesk()
    {
        var result = await Build().List("Weird", null);
        StatusOf(result).Should().Be(400);
        _client.Verify(c => c.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateTopicRequest_validates_group_slug()
    {
        var result = await Build().CreateTopicRequest(new CreateTopicRequestBody("Konu", "Bad Group", null));
        StatusOf(result).Should().Be(400);
        _client.Verify(c => c.CreateTopicRequestAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
