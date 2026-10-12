using IonCrm.Domain.Common;
using IonCrm.Domain.Enums;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Editable profile copy for one of Liftdesk's social accounts (Facebook Page / Instagram), per language.
/// Fields are a free key→value map (each provider has its own set, defaults seeded in code) so the
/// screen can grow without schema changes. Facebook fields can be pushed through the Graph API
/// (pages_manage_metadata); Instagram has no profile-write API, so its copy is a copy-paste source and
/// the operator confirms by hand. Images (profile picture, cover) live in SocialPostImages.
/// </summary>
public class SocialProfile : BaseEntity
{
    public SocialProvider Provider { get; set; }

    /// <summary>tr | en | en-GB — one profile per (provider, language).</summary>
    public string Language { get; set; } = "tr";

    /// <summary>JSON object: field key → text.</summary>
    public string FieldsJson { get; set; } = "{}";

    public Guid? ProfileImageId { get; set; }

    public Guid? CoverImageId { get; set; }

    /// <summary>Last successful push to the platform (Facebook) or manual confirmation (Instagram).</summary>
    public DateTime? AppliedAt { get; set; }

    public string? ApplyError { get; set; }

    public string? UpdatedBy { get; set; }
}
