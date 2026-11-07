using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.ReleaseNotes;

/// <summary>"Yenilikler" ekranı: Liftdesk yükü + bu kullanıcının görüldü durumu.</summary>
public record ReleaseNotesDto(
    string Language,
    string? GeneratedAt,
    /// <summary>En yeni en başta; kimliği boş kayıtlar ayıklanır.</summary>
    IReadOnlyList<LiftdeskRelease> Releases,
    string? LastSeenReleaseId,
    /// <summary>Kullanıcının henüz görmediği sürüm sayısı (bkz. <see cref="ReleaseNotesStatus.UnseenCount"/>).</summary>
    int UnseenCount);

/// <summary>Menü rozeti için hafif durum. Liftdesk'e ulaşılamazsa rozet sessiz kalır (UnseenCount 0).</summary>
public record ReleaseNotesStatusDto(
    bool Configured,
    string? LatestReleaseId,
    string? LatestTitle,
    string? LatestPeriodEnd,
    string? LastSeenReleaseId,
    int UnseenCount);
