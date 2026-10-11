using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// A custom image uploaded in the CRM for a Liftdesk social-media post (contract §6.3: the CRM keeps
/// the file, Liftdesk only stores its URL). Served anonymously at
/// <c>/api/v1/social-posts/images/{id}</c> because the publisher and, later, the platforms fetch it
/// without CRM credentials — the GUID is the only handle, content never changes once written.
/// Infrastructure-wide (Liftdesk's own marketing), no tenant scope.
/// </summary>
public class SocialPostImage : BaseEntity
{
    /// <summary>The Liftdesk post this image was uploaded for (used for cleanup).</summary>
    public Guid PostId { get; set; }

    /// <summary>Original file name, when known.</summary>
    public string? FileName { get; set; }

    /// <summary>MIME type: image/png, image/jpeg or image/webp.</summary>
    public string ContentType { get; set; } = "image/png";

    /// <summary>The raw file bytes (≤ 5 MB, enforced at upload).</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>"Custom" (operator upload) or "Card" (template card stored by the publisher for Instagram).</summary>
    public string Kind { get; set; } = "Custom";

    /// <summary>Card images only: variant language the card was rendered for.</summary>
    public string? Language { get; set; }
}
