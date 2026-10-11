using FluentAssertions;
using IonCrm.API.Common;
using IonCrm.API.Controllers;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models.ExternalApis;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace IonCrm.Tests.SocialPosts;

/// <summary>
/// The CRM side of the social-media approval screen is a thin proxy, so what matters is what it
/// refuses before calling Liftdesk (Turkish 400s with the same limits as Liftdesk's validator), how
/// it maps Liftdesk's status codes, and the image upload path (sniffed type, orphan cleanup).
/// </summary>
public class SocialPostsControllerTests
{
    private readonly Mock<ILiftdeskSocialPostsClient> _client = new();
    private readonly Mock<ISocialPostImageRepository> _images = new();

    private SocialPostsController Build(string? publicBaseUrl = "https://crm.example.com")
    {
        _client.SetupGet(c => c.IsConfigured).Returns(true);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SocialPosts:PublicApiBaseUrl"] = publicBaseUrl })
            .Build();
        var controller = new SocialPostsController(_client.Object, _images.Object, config, NullLogger<SocialPostsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return controller;
    }

    private static LiftdeskSocialPost Post(string status = "Pending", string? customImageUrl = null) => new(
        Guid.NewGuid(), "News", status, "2026-10-12:news", new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
        "Konu", null, null, customImageUrl, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow,
        new List<LiftdeskSocialPostVariant> { new("tr", "Başlık", "Metin", "Kısa", "#a") });

    private static LiftdeskEnvelope<T> Ok<T>(T data) => new(true, data, null, null, 200);
    private static LiftdeskEnvelope<T> Err<T>(int status, string? message = null) => new(false, default, message, null, status);

    private static int StatusOf(IActionResult result) => (result as ObjectResult)?.StatusCode ?? (result as StatusCodeResult)?.StatusCode ?? 0;
    private static string FirstError(IActionResult result) => ((ApiResponse<object>)((ObjectResult)result).Value!).Errors[0];

    // ── Validation before Liftdesk ───────────────────────────────────────────

    [Fact]
    public async Task Update_rejects_x_text_over_280_without_calling_liftdesk()
    {
        var body = new UpdateSocialPostBody(null, null, new List<SocialPostVariantBody>
        {
            new("tr", "Başlık", "Metin", new string('x', 281), "#a"),
        });

        var result = await Build().Update(Guid.NewGuid(), body);

        StatusOf(result).Should().Be(400);
        FirstError(result).Should().Contain("280");
        _client.Verify(c => c.UpdateAsync(It.IsAny<Guid>(), It.IsAny<LiftdeskSocialPostUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("")]
    public async Task Update_rejects_unknown_language(string language)
    {
        var body = new UpdateSocialPostBody(null, null, new List<SocialPostVariantBody> { new(language, "B", "M", "K", null) });

        var result = await Build().Update(Guid.NewGuid(), body);

        StatusOf(result).Should().Be(400);
        FirstError(result).Should().Contain("tr | en | en-GB");
    }

    [Fact]
    public async Task Update_normalises_language_casing_and_trims_before_sending()
    {
        var id = Guid.NewGuid();
        LiftdeskSocialPostUpdate? sent = null;
        _client.Setup(c => c.UpdateAsync(id, It.IsAny<LiftdeskSocialPostUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, LiftdeskSocialPostUpdate, CancellationToken>((_, u, _) => sent = u)
            .ReturnsAsync(Ok(Post()));
        var body = new UpdateSocialPostBody(" Yeni konu ", null, new List<SocialPostVariantBody>
        {
            new("EN-gb", " Title ", "Body", "Short", null),
        });

        var result = await Build().Update(id, body);

        StatusOf(result).Should().Be(200);
        sent.Should().NotBeNull();
        sent!.Topic.Should().Be("Yeni konu");
        sent.Variants.Should().ContainSingle().Which.Language.Should().Be("en-GB");
        sent.Variants![0].CardTitle.Should().Be("Title");
        sent.Variants[0].Hashtags.Should().Be(string.Empty);
        // No image change requested → no extra GET for cleanup bookkeeping.
        _client.Verify(c => c.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_rejects_unknown_status_and_normalises_known_one()
    {
        var controller = Build();

        var bad = await controller.List("Draft", null, null);
        StatusOf(bad).Should().Be(400);

        _client.Setup(c => c.ListAsync("Pending", null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(new LiftdeskSocialPostPage(new List<LiftdeskSocialPost>(), 0, 1, 100)));
        var ok = await controller.List("pending", null, null, page: 0, pageSize: 500);
        StatusOf(ok).Should().Be(200);
        _client.Verify(c => c.ListAsync("Pending", null, null, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Status-code mapping ──────────────────────────────────────────────────

    [Fact]
    public async Task Approve_maps_liftdesk_409_to_conflict_with_turkish_message()
    {
        var id = Guid.NewGuid();
        _client.Setup(c => c.ApproveAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Err<LiftdeskSocialPost>(409, "Post not found or not in an approvable state."));

        var result = await Build().Approve(id);

        StatusOf(result).Should().Be(409);
        FirstError(result).Should().Contain("onaylanabilir durumda değil");
    }

    [Fact]
    public async Task Delete_maps_404_to_published_cannot_be_deleted_message()
    {
        var id = Guid.NewGuid();
        _client.Setup(c => c.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Ok(Post("Published")));
        _client.Setup(c => c.DeleteAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Err<object>(404));

        var result = await Build().Delete(id);

        StatusOf(result).Should().Be(404);
        FirstError(result).Should().Contain("yayınlanan silinemez");
        _images.Verify(i => i.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_removes_own_image_blob_after_liftdesk_delete()
    {
        var id = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        _client.Setup(c => c.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Post("Pending", $"https://crm.example.com/api/v1/social-posts/images/{imageId}")));
        _client.Setup(c => c.DeleteAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Ok<object>(new { deleted = true }));

        var result = await Build().Delete(id);

        StatusOf(result).Should().Be(200);
        _images.Verify(i => i.DeleteAsync(imageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Image upload ─────────────────────────────────────────────────────────

    private static IFormFile FormFile(byte[] bytes, string name = "kart.png", string contentType = "image/png")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };

    [Fact]
    public async Task UploadImage_rejects_non_image_bytes_even_with_image_content_type()
    {
        var result = await Build().UploadImage(Guid.NewGuid(), FormFile("hello world"u8.ToArray()));

        StatusOf(result).Should().Be(400);
        FirstError(result).Should().Contain("PNG, JPEG veya WebP");
        _images.Verify(i => i.AddAsync(It.IsAny<SocialPostImage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadImage_refuses_approved_post_before_storing_anything()
    {
        var id = Guid.NewGuid();
        _client.Setup(c => c.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Ok(Post("Approved")));

        var result = await Build().UploadImage(id, FormFile(PngBytes));

        StatusOf(result).Should().Be(409);
        _images.Verify(i => i.AddAsync(It.IsAny<SocialPostImage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadImage_stores_blob_writes_public_url_and_drops_previous_own_image()
    {
        var id = Guid.NewGuid();
        var oldImage = Guid.NewGuid();
        _client.Setup(c => c.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(Post("Pending", $"https://crm.example.com/api/v1/social-posts/images/{oldImage}")));
        SocialPostImage? stored = null;
        _images.Setup(i => i.AddAsync(It.IsAny<SocialPostImage>(), It.IsAny<CancellationToken>()))
            .Callback<SocialPostImage, CancellationToken>((img, _) => stored = img)
            .Returns(Task.CompletedTask);
        LiftdeskSocialPostUpdate? sent = null;
        _client.Setup(c => c.UpdateAsync(id, It.IsAny<LiftdeskSocialPostUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, LiftdeskSocialPostUpdate, CancellationToken>((_, u, _) => sent = u)
            .Returns((Guid _, LiftdeskSocialPostUpdate u, CancellationToken _) => Task.FromResult(Ok(Post("Pending", u.CustomImageUrl))));

        var result = await Build().UploadImage(id, FormFile(PngBytes));

        StatusOf(result).Should().Be(200);
        stored.Should().NotBeNull();
        stored!.ContentType.Should().Be("image/png");
        stored.PostId.Should().Be(id);
        sent!.CustomImageUrl.Should().Be($"https://crm.example.com/api/v1/social-posts/images/{stored.Id}");
        sent.Topic.Should().BeNull();
        sent.Variants.Should().BeNull();
        _images.Verify(i => i.DeleteAsync(oldImage, It.IsAny<CancellationToken>()), Times.Once);
        _images.Verify(i => i.DeleteAsync(stored.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadImage_removes_stored_blob_when_liftdesk_refuses()
    {
        var id = Guid.NewGuid();
        _client.Setup(c => c.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Ok(Post("Pending")));
        SocialPostImage? stored = null;
        _images.Setup(i => i.AddAsync(It.IsAny<SocialPostImage>(), It.IsAny<CancellationToken>()))
            .Callback<SocialPostImage, CancellationToken>((img, _) => stored = img)
            .Returns(Task.CompletedTask);
        _client.Setup(c => c.UpdateAsync(id, It.IsAny<LiftdeskSocialPostUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Err<LiftdeskSocialPost>(409));

        var result = await Build().UploadImage(id, FormFile(PngBytes));

        StatusOf(result).Should().Be(409);
        _images.Verify(i => i.DeleteAsync(stored!.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetImage_serves_bytes_with_stored_content_type_and_404_when_missing()
    {
        var imageId = Guid.NewGuid();
        _images.Setup(i => i.GetAsync(imageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialPostImage { Id = imageId, PostId = Guid.NewGuid(), ContentType = "image/webp", Content = new byte[] { 1, 2, 3 } });
        var controller = Build();

        var found = await controller.GetImage(imageId);
        var missing = await controller.GetImage(Guid.NewGuid());

        found.Should().BeOfType<FileContentResult>().Which.ContentType.Should().Be("image/webp");
        ((FileContentResult)found).FileContents.Should().Equal(1, 2, 3);
        missing.Should().BeOfType<NotFoundResult>();
    }

    // ── Link helper ──────────────────────────────────────────────────────────

    [Fact]
    public void ImageLinks_recognise_only_own_urls()
    {
        var id = Guid.NewGuid();
        var own = SocialPostImageLinks.Build("https://crm.example.com/", id);

        own.Should().Be($"https://crm.example.com/api/v1/social-posts/images/{id}");
        SocialPostImageLinks.TryParseImageId(own).Should().Be(id);
        SocialPostImageLinks.TryParseImageId(own + "?v=2").Should().Be(id);
        SocialPostImageLinks.TryParseImageId("https://cdn.example.org/img/kart.png").Should().BeNull();
        SocialPostImageLinks.TryParseImageId(null).Should().BeNull();
        SocialPostImageLinks.TryParseImageId("").Should().BeNull();
    }

    [Fact]
    public void SniffImageType_identifies_png_jpeg_webp_and_rejects_others()
    {
        SocialPostImageLinks.SniffImageType(PngBytes).Should().Be("image/png");
        SocialPostImageLinks.SniffImageType(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }).Should().Be("image/jpeg");
        SocialPostImageLinks.SniffImageType("RIFF\0\0\0\0WEBPVP8 "u8).Should().Be("image/webp");
        SocialPostImageLinks.SniffImageType("GIF89a"u8).Should().BeNull();
        SocialPostImageLinks.SniffImageType(ReadOnlySpan<byte>.Empty).Should().BeNull();
    }
}
