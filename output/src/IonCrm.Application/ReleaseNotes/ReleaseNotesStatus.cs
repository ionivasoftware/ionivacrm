using System.Globalization;
using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.ReleaseNotes;

/// <summary>
/// Pure helpers shared by the release-notes handlers: which releases count as unseen for a user, and the
/// badge status derived from a Liftdesk payload.
/// </summary>
public static class ReleaseNotesStatus
{
    /// <summary>A user who never opened the screen sees releases from the last N days as new (not the whole history).</summary>
    public const int NeverSeenWindowDays = 30;

    /// <summary>Releases with a usable id, newest first (as delivered).</summary>
    public static IReadOnlyList<LiftdeskRelease> Usable(LiftdeskReleaseNotes? data) =>
        (data?.Releases ?? new List<LiftdeskRelease>()).Where(r => !string.IsNullOrWhiteSpace(r.Id)).ToList();

    /// <summary>
    /// Number of releases ahead of the one the user saw last. Unknown / never-seen id → releases whose
    /// <c>periodEnd</c> falls within the last <see cref="NeverSeenWindowDays"/> days (undated ones don't count).
    /// </summary>
    public static int UnseenCount(IReadOnlyList<LiftdeskRelease> releases, string? lastSeenId, DateOnly today)
    {
        if (releases.Count == 0) return 0;
        if (!string.IsNullOrEmpty(lastSeenId))
        {
            for (var i = 0; i < releases.Count; i++)
                if (string.Equals(releases[i].Id, lastSeenId, StringComparison.Ordinal)) return i;
        }
        var floor = today.AddDays(-NeverSeenWindowDays);
        return releases.Count(r => TryDate(r.PeriodEnd, out var end) && end >= floor);
    }

    /// <summary>Badge status for a payload (or for "nothing usable" when <paramref name="data"/> is null).</summary>
    public static ReleaseNotesStatusDto Build(LiftdeskReleaseNotes? data, string? lastSeenId, DateOnly today, bool configured = true)
    {
        var releases = Usable(data);
        var latest = releases.Count > 0 ? releases[0] : null;
        return new ReleaseNotesStatusDto(
            configured,
            latest?.Id,
            latest?.Title,
            latest?.PeriodEnd,
            lastSeenId,
            UnseenCount(releases, lastSeenId, today));
    }

    /// <summary>yyyy-MM-dd düz metin (sözleşme); başka biçim → false.</summary>
    public static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
