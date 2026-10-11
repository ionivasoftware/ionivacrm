using IonCrm.Domain.Enums;

namespace IonCrm.Application.Features.SocialPublishing;

/// <summary>
/// Pure scheduling rules of the publisher — no I/O, fully unit-tested. EMS holds only the publish DAY;
/// the hour policy, lateness cut-off, retry backoff and the stamp condition live here.
/// </summary>
public static class SocialPublishPlanner
{
    public const int MaxAttempts = 8;

    /// <summary>Default local publish hour (TR). Overridable via SocialPublisher:PublishHourLocal.</summary>
    public const int DefaultPublishHour = 10;

    /// <summary>Default lateness cut-off: a post more than this many days past its day is NOT published (stale news / past holiday).</summary>
    public const int DefaultMaxLateDays = 2;

    /// <summary>Local calendar day of the publish DAY (EMS stores 00:00Z; the date part is the day).</summary>
    public static DateOnly ScheduledDay(DateTime scheduledForUtc) => DateOnly.FromDateTime(scheduledForUtc);

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);

    /// <summary>Due = day has arrived locally and, on the day itself, the publish hour has passed.</summary>
    public static bool IsDue(DateTime scheduledForUtc, DateTime nowUtc, TimeZoneInfo tz, int publishHour)
    {
        var day = ScheduledDay(scheduledForUtc);
        var nowLocal = ToLocal(nowUtc, tz);
        var today = DateOnly.FromDateTime(nowLocal);
        if (day < today) return true;
        if (day > today) return false;
        return nowLocal.Hour >= publishHour;
    }

    /// <summary>Too late to publish: the day is more than <paramref name="maxLateDays"/> days behind local today.</summary>
    public static bool IsStale(DateTime scheduledForUtc, DateTime nowUtc, TimeZoneInfo tz, int maxLateDays)
    {
        var day = ScheduledDay(scheduledForUtc);
        var today = DateOnly.FromDateTime(ToLocal(nowUtc, tz));
        return today.DayNumber - day.DayNumber > maxLateDays;
    }

    /// <summary>15 min, 30 min, 1 h, 2 h, 4 h, 8 h, 12 h, then 24 h.</summary>
    public static TimeSpan Backoff(int attemptsSoFar) => attemptsSoFar switch
    {
        <= 1 => TimeSpan.FromMinutes(15),
        2 => TimeSpan.FromMinutes(30),
        3 => TimeSpan.FromHours(1),
        4 => TimeSpan.FromHours(2),
        5 => TimeSpan.FromHours(4),
        6 => TimeSpan.FromHours(8),
        7 => TimeSpan.FromHours(12),
        _ => TimeSpan.FromHours(24),
    };

    /// <summary>Facebook / Instagram caption: body, blank line, hashtags (EMS keeps them apart).</summary>
    public static string ComposeCaption(string body, string? hashtags)
    {
        var b = body.Trim();
        var h = hashtags?.Trim();
        return string.IsNullOrEmpty(h) ? b : $"{b}\n\n{h}";
    }

    /// <summary>
    /// Stamp EMS only when every job is settled as Succeeded or Skipped and at least one actually
    /// succeeded — "Published" must mean something went out somewhere.
    /// </summary>
    public static bool ReadyToStamp(IReadOnlyCollection<SocialPublishJobStatus> statuses)
    {
        if (statuses.Count == 0) return false;
        var anySuccess = false;
        foreach (var s in statuses)
        {
            if (s == SocialPublishJobStatus.Succeeded) anySuccess = true;
            else if (s != SocialPublishJobStatus.Skipped) return false;
        }
        return anySuccess;
    }

    /// <summary>Which languages of a post have an enabled, mapped account — the jobs to create.</summary>
    public static IEnumerable<(Guid AccountId, SocialProvider Provider, string Language)> PlanJobs(
        IEnumerable<(Guid AccountId, SocialProvider Provider, string? Language, bool Usable)> accounts,
        IEnumerable<string> postLanguages)
    {
        var langs = new HashSet<string>(postLanguages, StringComparer.OrdinalIgnoreCase);
        foreach (var a in accounts)
        {
            if (!a.Usable || string.IsNullOrWhiteSpace(a.Language)) continue;
            if (!langs.Contains(a.Language)) continue;
            yield return (a.AccountId, a.Provider, a.Language);
        }
    }

    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        foreach (var candidate in new[] { id, "Europe/Istanbul", "Turkey Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("TR+3", TimeSpan.FromHours(3), "TR+3", "TR+3");
    }
}
