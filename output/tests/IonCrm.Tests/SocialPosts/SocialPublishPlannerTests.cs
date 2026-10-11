using FluentAssertions;
using IonCrm.Application.Features.SocialPublishing;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.SocialPosts;

/// <summary>
/// The publisher's scheduling rules are the part worth pinning down: EMS gives only a DAY, the CRM
/// decides the hour, lateness, retries and when "Published" may be stamped back.
/// </summary>
public class SocialPublishPlannerTests
{
    private static readonly TimeZoneInfo Tr = TimeZoneInfo.CreateCustomTimeZone("TR+3", TimeSpan.FromHours(3), "TR+3", "TR+3");
    private static readonly DateTime Day = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc); // publish day (00:00Z as EMS stores it)

    [Theory]
    [InlineData("2026-10-12T06:30:00Z", false)] // 09:30 TR — hour not reached
    [InlineData("2026-10-12T07:00:00Z", true)]  // 10:00 TR — due
    [InlineData("2026-10-12T20:00:00Z", true)]  // later same day
    [InlineData("2026-10-13T01:00:00Z", true)]  // next day — overdue, still due
    [InlineData("2026-10-11T20:00:00Z", false)] // 23:00 TR the day before
    [InlineData("2026-10-11T21:30:00Z", false)] // 00:30 TR on the day, but before 10:00
    public void IsDue_follows_local_day_and_publish_hour(string nowIso, bool expected)
    {
        var now = DateTime.Parse(nowIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        SocialPublishPlanner.IsDue(Day, now, Tr, 10).Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-10-14T12:00:00Z", 2, false)] // 2 days late — still allowed
    [InlineData("2026-10-15T12:00:00Z", 2, true)]  // 3 days late — stale
    [InlineData("2026-10-15T12:00:00Z", 5, false)]
    public void IsStale_uses_max_late_days(string nowIso, int maxLate, bool expected)
    {
        var now = DateTime.Parse(nowIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        SocialPublishPlanner.IsStale(Day, now, Tr, maxLate).Should().Be(expected);
    }

    [Fact]
    public void Backoff_grows_then_caps_at_a_day()
    {
        SocialPublishPlanner.Backoff(1).Should().Be(TimeSpan.FromMinutes(15));
        SocialPublishPlanner.Backoff(2).Should().Be(TimeSpan.FromMinutes(30));
        SocialPublishPlanner.Backoff(3).Should().Be(TimeSpan.FromHours(1));
        SocialPublishPlanner.Backoff(7).Should().Be(TimeSpan.FromHours(12));
        SocialPublishPlanner.Backoff(8).Should().Be(TimeSpan.FromHours(24));
        SocialPublishPlanner.Backoff(40).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void ComposeCaption_appends_hashtags_after_blank_line_and_keeps_body_alone_otherwise()
    {
        SocialPublishPlanner.ComposeCaption(" Gövde ", "#asansör #bakım").Should().Be("Gövde\n\n#asansör #bakım");
        SocialPublishPlanner.ComposeCaption("Gövde", "  ").Should().Be("Gövde");
        SocialPublishPlanner.ComposeCaption("Gövde", null).Should().Be("Gövde");
    }

    [Fact]
    public void ReadyToStamp_requires_all_settled_and_at_least_one_success()
    {
        SocialPublishPlanner.ReadyToStamp(Array.Empty<SocialPublishJobStatus>()).Should().BeFalse("nothing went out");
        SocialPublishPlanner.ReadyToStamp(new[] { SocialPublishJobStatus.Succeeded }).Should().BeTrue();
        SocialPublishPlanner.ReadyToStamp(new[] { SocialPublishJobStatus.Succeeded, SocialPublishJobStatus.Skipped }).Should().BeTrue();
        SocialPublishPlanner.ReadyToStamp(new[] { SocialPublishJobStatus.Skipped }).Should().BeFalse("only skips — nothing published");
        SocialPublishPlanner.ReadyToStamp(new[] { SocialPublishJobStatus.Succeeded, SocialPublishJobStatus.Pending }).Should().BeFalse();
        SocialPublishPlanner.ReadyToStamp(new[] { SocialPublishJobStatus.Succeeded, SocialPublishJobStatus.Failed }).Should().BeFalse();
        SocialPublishPlanner.ReadyToStamp(new[] { SocialPublishJobStatus.Succeeded, SocialPublishJobStatus.Blocked }).Should().BeFalse();
        SocialPublishPlanner.ReadyToStamp(new[] { SocialPublishJobStatus.Succeeded, SocialPublishJobStatus.InProgress }).Should().BeFalse();
    }

    [Fact]
    public void PlanJobs_maps_each_usable_account_to_its_language_variant_only()
    {
        var fbTr = Guid.NewGuid(); var igTr = Guid.NewGuid(); var xUk = Guid.NewGuid(); var disabled = Guid.NewGuid(); var unmapped = Guid.NewGuid();
        var accounts = new[]
        {
            (fbTr, SocialProvider.Facebook, (string?)"tr", true),
            (igTr, SocialProvider.Instagram, (string?)"TR", true),       // case-insensitive
            (xUk, SocialProvider.X, (string?)"en-GB", true),
            (disabled, SocialProvider.X, (string?)"tr", false),
            (unmapped, SocialProvider.Facebook, (string?)null, true),
        };

        // Post only has tr and en variants — en-GB account gets nothing (not an error, contract §3).
        var plan = SocialPublishPlanner.PlanJobs(accounts, new[] { "tr", "en" }).ToList();

        plan.Select(p => p.AccountId).Should().BeEquivalentTo(new[] { fbTr, igTr });
        plan.Should().OnlyContain(p => p.Language.Equals("tr", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveTimeZone_falls_back_to_a_fixed_plus_three_offset()
    {
        var tz = SocialPublishPlanner.ResolveTimeZone("No/Such_Zone");
        tz.BaseUtcOffset.Should().Be(TimeSpan.FromHours(3));
    }
}
