using FluentAssertions;
using IonCrm.Application.Features.CallPlan;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.CallPlan;

/// <summary>The CSO planner's rules: who gets called, in which order, and how a week is filled.</summary>
public class CallPlannerTests
{
    private static readonly DateOnly Today = new(2026, 10, 19); // Monday
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

    private static PlanCandidate Lead(string name, CustomerLabel? label = null, int? daysSinceContact = null, int ageDays = 100, int contacts = 0, DateTime? next = null) =>
        new(Guid.NewGuid(), name, CustomerStatus.Lead, label, Now.AddDays(-ageDays),
            daysSinceContact is { } d ? Now.AddDays(-d) : null, contacts, null, next, null, false, null, null);

    private static PlanCandidate Active(string name, int? daysSinceContact = null, DateTime? expiration = null, UsageSignal? usage = null, int? lastSatDays = null, DateTime? next = null, CustomerStatus status = CustomerStatus.Active) =>
        new(Guid.NewGuid(), name, status, null, Now.AddDays(-400),
            daysSinceContact is { } d ? Now.AddDays(-d) : null, daysSinceContact is null ? 0 : 3,
            lastSatDays is { } s ? Now.AddDays(-s) : null, next, expiration, false, null, usage);

    private static UsageSignal Usage(int? loginDaysAgo, int maintenance, int elevators = 10, string? plan = null) =>
        new(loginDaysAgo is { } l ? Now.AddDays(-l) : null, maintenance, 0, 0, 0, 0, elevators, 3, plan, 2026, 10);

    [Fact]
    public void BusinessDays_skips_weekends_and_StartDay_moves_after_17()
    {
        CallPlanner.BusinessDays(new DateOnly(2026, 10, 16), 3).Should().Equal(new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 20)); // Fri, Mon, Tue
        CallPlanner.StartDay(new DateTime(2026, 10, 19, 16, 59, 0)).Should().Be(new DateOnly(2026, 10, 19));
        CallPlanner.StartDay(new DateTime(2026, 10, 19, 17, 0, 0)).Should().Be(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public void NewCall_ranks_high_potential_untouched_leads_first_and_skips_recent_contacts()
    {
        var hot = CallPlanner.ScoreNewCall(Lead("Hot", CustomerLabel.YuksekPotansiyel), Today, Today.AddDays(6))!.Value;
        var cold = CallPlanner.ScoreNewCall(Lead("Cold", CustomerLabel.Kotu, daysSinceContact: 20, contacts: 6), Today, Today.AddDays(6))!.Value;
        hot.Score.Should().BeGreaterThan(cold.Score);
        hot.Reason.Should().Contain("Yüksek potansiyel").And.Contain("hiç görüşülmedi");
        cold.Reason.Should().Contain("çok denendi");

        CallPlanner.ScoreNewCall(Lead("Recent", CustomerLabel.Potansiyel, daysSinceContact: 3), Today, Today.AddDays(6)).Should().BeNull("7-day cooldown");
        CallPlanner.ScoreNewCall(Lead("Later", CustomerLabel.Potansiyel, daysSinceContact: 30, next: Now.AddDays(40)), Today, Today.AddDays(6)).Should().BeNull("asked to be called later");
        CallPlanner.ScoreNewCall(Active("NotLead"), Today, Today.AddDays(6)).Should().BeNull();
    }

    [Fact]
    public void FollowUp_scores_promises_renewals_and_silent_usage()
    {
        var promised = CallPlanner.ScoreFollowUp(Active("Promise", daysSinceContact: 2, next: Now.AddDays(-1)), Today)!.Value;
        promised.Score.Should().BeGreaterThanOrEqualTo(100);
        promised.Reason.Should().Contain("söz verilen takip");

        var renewal = CallPlanner.ScoreFollowUp(Active("Renew", daysSinceContact: 40, expiration: Now.AddDays(20)), Today)!.Value;
        renewal.Reason.Should().Contain("süresi 20 gün içinde doluyor");

        var silent = CallPlanner.ScoreFollowUp(Active("Silent", daysSinceContact: 50, usage: Usage(loginDaysAgo: 30, maintenance: 0)), Today)!.Value;
        silent.Reason.Should().Contain("30 gün giriş yok").And.Contain("bu ay bakım girişi yok");
        silent.Score.Should().BeGreaterThan(renewal.Score - 40);

        CallPlanner.ScoreFollowUp(Active("Fresh", daysSinceContact: 5), Today).Should().BeNull("14-day cooldown");
        CallPlanner.ScoreFollowUp(Active("Churned long ago", daysSinceContact: 200, expiration: Now.AddDays(-200), status: CustomerStatus.Churned), Today).Should().BeNull();
        CallPlanner.ScoreFollowUp(Active("Churned recently", daysSinceContact: 60, expiration: Now.AddDays(-20), status: CustomerStatus.Churned), Today)!.Value.Reason.Should().Contain("geri kazanım");
    }

    [Fact]
    public void Satisfaction_targets_healthy_active_customers_not_called_recently()
    {
        var never = CallPlanner.ScoreSatisfaction(Active("Happy", daysSinceContact: 120, usage: Usage(loginDaysAgo: 2, maintenance: 12)), Today)!.Value;
        never.Reason.Should().Contain("hiç memnuniyet araması yapılmadı").And.Contain("yoğun kullanım");

        CallPlanner.ScoreSatisfaction(Active("Inactive", daysSinceContact: 120, usage: Usage(loginDaysAgo: 40, maintenance: 0)), Today).Should().BeNull("unhealthy → follow-up, not satisfaction");
        CallPlanner.ScoreSatisfaction(Active("Recent sat", daysSinceContact: 120, usage: Usage(1, 5), lastSatDays: 150), Today).Should().BeNull("180-day repeat window");
        CallPlanner.ScoreSatisfaction(Active("Old sat", daysSinceContact: 120, usage: Usage(1, 5), lastSatDays: 200), Today).Should().NotBeNull("past the 180-day window");
        CallPlanner.ScoreSatisfaction(Active("Just talked", daysSinceContact: 45, usage: Usage(1, 5)), Today).Should().BeNull("60-day cooldown");
        CallPlanner.ScoreSatisfaction(Active("Demo", daysSinceContact: 120, status: CustomerStatus.Demo), Today).Should().BeNull();
    }

    [Fact]
    public void Plan_fills_daily_quotas_without_repeating_a_company_and_respects_existing_slots()
    {
        var leads = Enumerable.Range(0, 12).Select(i => Lead($"Lead{i}", CustomerLabel.Potansiyel)).ToList();
        var actives = Enumerable.Range(0, 7).Select(i => Active($"Act{i}", daysSinceContact: 60 + i, usage: Usage(30, 0))).ToList();
        var happy = Enumerable.Range(0, 3).Select(i => Active($"Happy{i}", daysSinceContact: 120, usage: Usage(1, 10))).ToList();
        var all = leads.Concat(actives).Concat(happy).ToList();
        var days = CallPlanner.BusinessDays(Today, 2);
        var existing = new Dictionary<(DateOnly, AppointmentKind), int> { [(Today, AppointmentKind.NewCall)] = 2 };
        var alreadyPlanned = new HashSet<Guid> { leads[0].CustomerId };

        var plan = CallPlanner.Plan(all, days, new CallPlanQuotas(5, 5, 2), new CallPlanSlots(), alreadyPlanned, existing, Today);

        plan.Select(p => p.CustomerId).Should().OnlyHaveUniqueItems("a company is called at most once in the horizon");
        plan.Should().NotContain(p => p.CustomerId == leads[0].CustomerId, "already on the calendar");
        plan.Count(p => p.Day == Today && p.Kind == AppointmentKind.NewCall).Should().Be(3, "2 already planned today");
        plan.Count(p => p.Day == days[1] && p.Kind == AppointmentKind.NewCall).Should().Be(5);
        plan.Count(p => p.Day == Today && p.Kind == AppointmentKind.FollowUpCall).Should().Be(5);
        plan.Count(p => p.Day == days[1] && p.Kind == AppointmentKind.FollowUpCall).Should().Be(2, "only 7 eligible actives");
        plan.Count(p => p.Kind == AppointmentKind.SatisfactionCall).Should().Be(3, "3 healthy customers, quota 2+2");
        plan.Where(p => p.Day == Today && p.Kind == AppointmentKind.NewCall).Select(p => p.Time).Should().Equal(new TimeOnly(10, 30), new TimeOnly(10, 45), new TimeOnly(11, 0));
        plan.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Reason));
    }

    [Fact]
    public void Plan_puts_promised_callbacks_on_their_day_above_quota()
    {
        var day2 = Today.AddDays(1);
        var promised = Active("Promised", daysSinceContact: 1, next: day2.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc));
        var overdue = Active("Overdue", daysSinceContact: 10, next: Now.AddDays(-3));
        var filler = Enumerable.Range(0, 10).Select(i => Active($"F{i}", daysSinceContact: 60 + i, usage: Usage(30, 0))).ToList();

        var plan = CallPlanner.Plan(filler.Concat(new[] { promised, overdue }).ToList(), new[] { Today, day2 }, new CallPlanQuotas(0, 5, 0), new CallPlanSlots(),
            new HashSet<Guid>(), new Dictionary<(DateOnly, AppointmentKind), int>(), Today);

        plan.Should().ContainSingle(p => p.CustomerId == promised.CustomerId).Which.Day.Should().Be(day2);
        plan.Should().ContainSingle(p => p.CustomerId == overdue.CustomerId).Which.Day.Should().Be(Today, "past-due promise lands on the first day");
        plan.Where(p => p.CustomerId == promised.CustomerId || p.CustomerId == overdue.CustomerId).Should().OnlyContain(p => p.Score == 1000 && p.Reason.StartsWith("Söz verilen takip"));
        plan.Count(p => p.Day == Today && p.Kind == AppointmentKind.FollowUpCall).Should().Be(6, "quota 5 + 1 promise above quota");
    }

    [Fact]
    public void SlotTime_spaces_calls_and_never_overflows_the_day()
    {
        var slots = new CallPlanSlots();
        CallPlanner.SlotTime(AppointmentKind.FollowUpCall, 0, slots).Should().Be(new TimeOnly(14, 0));
        CallPlanner.SlotTime(AppointmentKind.SatisfactionCall, 1, slots).Should().Be(new TimeOnly(16, 45));
        CallPlanner.SlotTime(AppointmentKind.NewCall, 500, slots).Should().Be(new TimeOnly(23, 45));
    }
}
