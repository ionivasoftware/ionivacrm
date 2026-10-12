using IonCrm.Application.Common.Models;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>Symmetric encryption for platform tokens at rest (AES-GCM; key from Social:TokenKey, fallback JwtSettings:Secret).</summary>
public interface ITokenProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}

/// <summary>
/// Meta Graph API (Facebook Page + Instagram Business). One Meta app covers both platforms;
/// Instagram publishing uses the linked Page's token.
/// </summary>
public interface IMetaGraphClient
{
    bool IsConfigured { get; }

    string BuildAuthorizationUrl(string redirectUri, string state);

    /// <summary>Code → short-lived user token → long-lived user token (≈ 60 days).</summary>
    Task<MetaTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>Pages the user manages, each with its (long-lived) page token and linked IG business account.</summary>
    Task<List<MetaPage>> GetPagesAsync(string userToken, CancellationToken cancellationToken = default);

    /// <summary>Lightweight token check: GET /{id}?fields=id,name — throws <see cref="SocialPublishException"/> on failure.</summary>
    Task<string> GetDisplayNameAsync(string nodeId, string token, CancellationToken cancellationToken = default);

    /// <summary>POST /{page-id}/photos (url + message). Returns the created post id.</summary>
    Task<string> PublishFacebookPhotoAsync(string pageId, string pageToken, string imageUrl, string message, CancellationToken cancellationToken = default);

    /// <summary>POST /{page-id}/feed (message only) — used for text-only test posts.</summary>
    Task<string> PublishFacebookTextAsync(string pageId, string pageToken, string message, CancellationToken cancellationToken = default);

    /// <summary>Two-step: POST /{ig-user-id}/media (image_url, caption) → POST /{ig-user-id}/media_publish. Returns the IG media id.</summary>
    Task<string> PublishInstagramImageAsync(string igUserId, string token, string imageUrl, string caption, CancellationToken cancellationToken = default);

    /// <summary>POST /{page-id} with about / description / website / phone / emails — needs pages_manage_metadata.</summary>
    Task UpdatePageAsync(string pageId, string pageToken, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default);

    /// <summary>POST /{page-id}/picture (multipart "source") — sets the Page profile picture. Returns the created photo id when given.</summary>
    Task<string?> SetPagePictureAsync(string pageId, string pageToken, byte[] bytes, string contentType, CancellationToken cancellationToken = default);
}

/// <summary>X API v2 with OAuth 2.0 PKCE (tweet.read tweet.write users.read media.write offline.access).</summary>
public interface IXApiClient
{
    bool IsConfigured { get; }

    string BuildAuthorizationUrl(string redirectUri, string state, string codeChallenge);

    Task<XTokenResult> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken = default);

    Task<XTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<XUser> GetMeAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Uploads image bytes (media_category=tweet_image); returns the media id.</summary>
    Task<string> UploadImageAsync(string accessToken, byte[] bytes, string contentType, CancellationToken cancellationToken = default);

    /// <summary>POST /2/tweets — text verbatim (bodyShort already includes hashtags, ≤ 280 enforced by EMS).</summary>
    Task<string> CreatePostAsync(string accessToken, string text, string? mediaId, CancellationToken cancellationToken = default);
}

/// <summary>One scheduler cycle: discover due approved posts, create jobs, publish due jobs, stamp back.</summary>
public interface ISocialPublishRunner
{
    Task<SocialPublishRunSummary> RunOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Process-wide memory of the last cycle (singleton) so the status endpoint can show it.</summary>
public interface ISocialPublisherState
{
    SocialPublishRunSummary? LastRun { get; set; }
}
