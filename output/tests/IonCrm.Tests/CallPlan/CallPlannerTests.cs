using FluentAssertions;
using IonCrm.Application.Features.CallPlan;
using IonCrm.Domain.Enums;

namespace IonCrm.Tests.CallPlan;

/// <summary>
/// The CSO planner's rules: who gets called, in which pool, in which order, and how a week is filled.
/// Pools (02.11.2026): new call = never-reached lead/demo, passive, recently churned; follow-up = reached
/// lead/demo; satisfaction = the only planned call to an Active customer (risk signals scored there).
/// </summary>
public class CallPlannerTests
{
    private static readonly DateOnly Today = new(2026, 10, 19); // Monday
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly HorizonEnd = Today.AddDays(6);

    /// <param name="contacts">Reached conversations. 0 with <paramref name="daysSinceContact"/> set = unreached attempt.</param>
    private static PlanCandidate Lead(string name, CustomerLabel? label = null, int? daysSinceContact = null, int ageDays = 100, int contacts = 0, DateTime? next = null, CustomerStatus status = CustomerStatus.Lead, DateTime? expiration = null, UsageSignal? usage = null, int otherContacts = 0) =>
        new(Guid.NewGuid(), name, status, label, Now.AddDays(-ageDays),
            daysSinceContact is { } d ? Now.AddDays(-d) : null, contacts, null, next, expiration, false, null, usage, otherContacts);

    private static PlanCandidate Active(string name, int? daysSinceContact = null, DateTime? expiration = null, UsageSignal? usage = null, int? lastSatDays = null, DateTime? next = null, CustomerStatus status = CustomerStatus.Active) =>
        new(Guid.NewGuid(), name, status, null, Now.AddDays(-400),
            daysSinceContact is { } d ? Now.AddDays(-d) : null, daysSinceContact is null ? 0 : 3,
            lastSatDays is { } s ? Now.AddDays(-s) : null, next, expiration, false, null, usage);

    /// <param name="prevMaintenance">Previous month's maintenance count; null = no earlier snapshot. Snapshot month = the test's current month (Oct 2026).</param>
    private static UsageSignal Usage(int? loginDaysAgo, int maintenance, int elevators = 10, string? plan = null, int? prevMaintenance = null, int? prevFaults = null) =>
        new(loginDaysAgo is { } l ? Now.AddDays(-l) : null, maintenance, 0, 0, 0, 0, elevators, 3, plan, 2026, 10,
            prevMaintenance, prevFaults, prevFaults is null ? null : 0);

    [Fact]
    public void BusinessDays_skips_weekends_and_StartDay_moves_after_17()
    {
        CallPlanner.BusinessDays(new DateOnly(2026, 10, 16), 3).Should().Equal(new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 20)); // Fri, Mon, Tue
        CallPlanner.StartDay(new DateTime(2026, 10, 19, 16, 59, 0)).Should().Be(new DateOnly(2026, 10, 19));
        CallPlanner.StartDay(new DateTime(2026, 10, 19, 17, 0, 0)).Should().Be(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public void NewCall_pool_is_untouched_leads_passive_companies_and_recent_churn()
    {
        var hot = CallPlanner.ScoreNewCall(Lead("Hot", CustomerLabel.YuksekPotansiyel), Today, HorizonEnd)!.Value;
        hot.Reason.Should().StartWith("Lead").And.Contain("Yüksek potansiyel").And.Contain("hiç görüşülmedi");

        // Otomatik maillerle dolu zaman çizelgesi: görüşme sayılmaz ama gerekçe neyin olduğunu söyler (Apap Lifts, 09.11.2026).
        var mailed = CallPlanner.ScoreNewCall(Lead("Mailed demo", status: CustomerStatus.Demo, ageDays: 10, expiration: Now.AddDays(11), otherContacts: 4), Today, HorizonEnd)!.Value;
        mailed.Reason.Should().Contain("aranmadı (yalnız 4 e-posta/not kaydı)").And.NotContain("hiç görüşülmedi");
        mailed.Score.Should().Be(hot.Score - CallPlanner.LabelScore(CustomerLabel.YuksekPotansiyel) + CallPlanner.LabelScore(null) + 30 + 40 + 20,
            "mails change the wording only: same 'never talked' points, plus demo / expiry / new-record points");

        var unreached = CallPlanner.ScoreNewCall(Lead("NoAnswer", CustomerLabel.Kotu, daysSinceContact: 20, contacts: 0), Today, HorizonEnd)!.Value;
        unreached.Reason.Should().Contain("ulaşılamadı, son deneme 20 gün önce");
        hot.Score.Should().BeGreaterThan(unreached.Score);

        CallPlanner.ScoreNewCall(Lead("Recent", CustomerLabel.Potansiyel, daysSinceContact: 3), Today, HorizonEnd).Should().BeNull("7-day cooldown after an attempt");
        CallPlanner.ScoreNewCall(Lead("Later", CustomerLabel.Potansiyel, daysSinceContact: 30, next: Now.AddDays(40)), Today, HorizonEnd).Should().BeNull("asked to be called later");
        CallPlanner.ScoreNewCall(Lead("Spoken", CustomerLabel.Potansiyel, daysSinceContact: 30, contacts: 2), Today, HorizonEnd).Should().BeNull("a reached lead is a follow-up, not a new call");
        CallPlanner.ScoreNewCall(Active("Customer", daysSinceContact: 200, usage: Usage(40, 0)), Today, HorizonEnd).Should().BeNull("active customers only get satisfaction calls");

        var passive = CallPlanner.ScoreNewCall(Lead("Expired", daysSinceContact: 20, contacts: 1, status: CustomerStatus.Passive, expiration: Now.AddDays(-10)), Today, HorizonEnd)!.Value;
        passive.Reason.Should().StartWith("Pasif").And.Contain("denemesi 10 gün önce bitti");
        var oldPassive = CallPlanner.ScoreNewCall(Lead("Dormant", daysSinceContact: 600, contacts: 3, status: CustomerStatus.Passive, expiration: Now.AddDays(-500)), Today, HorizonEnd)!.Value;
        oldPassive.Reason.Should().StartWith("Pasif").And.NotContain("bitti").And.Contain("600 gün görüşülmedi");
        passive.Score.Should().BeGreaterThan(oldPassive.Score, "a trial that just expired is warmer than a long-dormant account");

        CallPlanner.ScoreNewCall(Active("Churned recently", daysSinceContact: 60, expiration: Now.AddDays(-20), status: CustomerStatus.Churned), Today, HorizonEnd)!.Value.Reason.Should().StartWith("Ayrılmış").And.Contain("geri kazanım");
        CallPlanner.ScoreNewCall(Active("Churned long ago", daysSinceContact: 200, expiration: Now.AddDays(-200), status: CustomerStatus.Churned), Today, HorizonEnd).Should().BeNull();

        var demo = CallPlanner.ScoreNewCall(Lead("Trial", ageDays: 2, status: CustomerStatus.Demo, expiration: Now.AddDays(5)), Today, HorizonEnd)!.Value;
        demo.Reason.Should().StartWith("Demo").And.Contain("hiç görüşülmedi").And.Contain("demo sürecinde").And.Contain("süresi 5 gün içinde doluyor").And.Contain("yeni kayıt");
    }

    [Fact]
    public void FollowUp_pool_is_leads_and_demos_that_were_reached_before()
    {
        var warm = CallPlanner.ScoreFollowUp(Lead("Warm", CustomerLabel.Potansiyel, daysSinceContact: 20, contacts: 2), Today)!.Value;
        warm.Reason.Should().StartWith("Lead").And.Contain("Potansiyel").And.Contain("2 görüşme yapıldı");

        CallPlanner.ScoreFollowUp(Lead("NoAnswer", CustomerLabel.Potansiyel, daysSinceContact: 20, contacts: 0), Today).Should().BeNull("never reached → new call pool");
        CallPlanner.ScoreFollowUp(Lead("Fresh", CustomerLabel.Potansiyel, daysSinceContact: 5, contacts: 1), Today).Should().BeNull("14-day cooldown");

        var promised = CallPlanner.ScoreFollowUp(Lead("Promise", daysSinceContact: 2, contacts: 1, next: Now.AddDays(-1)), Today)!.Value;
        promised.Score.Should().BeGreaterThanOrEqualTo(100);
        promised.Reason.Should().Contain("söz verilen takip");

        var demo = CallPlanner.ScoreFollowUp(Lead("Trial", daysSinceContact: 15, contacts: 1, status: CustomerStatus.Demo, expiration: Now.AddDays(10), usage: Usage(10, 0, plan: "Trial")), Today)!.Value;
        demo.Reason.Should().StartWith("Demo").And.Contain("demo sürecinde").And.Contain("süresi 10 gün içinde doluyor").And.Contain("10 gün giriş yok").And.Contain("deneme planı");
        demo.Score.Should().BeGreaterThan(warm.Score);

        CallPlanner.ScoreFollowUp(Active("Silent", daysSinceContact: 50, usage: Usage(loginDaysAgo: 30, maintenance: 0)), Today).Should().BeNull("active → satisfaction call");
        CallPlanner.ScoreFollowUp(Active("Passive", daysSinceContact: 60, status: CustomerStatus.Passive), Today).Should().BeNull("passive → new call");
        CallPlanner.ScoreFollowUp(Active("Churned", daysSinceContact: 60, expiration: Now.AddDays(-20), status: CustomerStatus.Churned), Today).Should().BeNull("churned → new call (win-back)");
    }

    [Fact]
    public void Satisfaction_is_the_only_call_to_active_customers_and_catches_risk_signals()
    {
        var happy = CallPlanner.ScoreSatisfaction(Active("Happy", daysSinceContact: 120, usage: Usage(loginDaysAgo: 2, maintenance: 12)), Today)!.Value;
        happy.Reason.Should().StartWith("Aktif müşteri").And.Contain("hiç memnuniyet araması yapılmadı").And.Contain("yoğun kullanım");

        var silent = CallPlanner.ScoreSatisfaction(Active("Silent", daysSinceContact: 20, usage: Usage(loginDaysAgo: 40, maintenance: 0, prevMaintenance: 0, prevFaults: 0), lastSatDays: 50), Today)!.Value;
        silent.Reason.Should().Contain("40 gün giriş yok").And.Contain("bir aydır bakım girişi yok").And.Contain("bir aydır arıza/iş emri yok");
        silent.Score.Should().BeGreaterThan(happy.Score, "a real usage dip (login gone, a full month without entries) outranks a routine courtesy call");
        CallPlanner.ScoreSatisfaction(Active("Silent but just called", daysSinceContact: 10, usage: Usage(40, 0)), Today).Should().BeNull("14-day cooldown even when at risk");

        var renewal = CallPlanner.ScoreSatisfaction(Active("Renew", daysSinceContact: 20, expiration: Now.AddDays(20), usage: Usage(1, 5), lastSatDays: 30), Today)!.Value;
        renewal.Reason.Should().Contain("süresi 20 gün içinde doluyor");

        CallPlanner.ScoreSatisfaction(Active("Recent sat", daysSinceContact: 120, usage: Usage(1, 5), lastSatDays: 150), Today).Should().BeNull("healthy: 180-day repeat window");
        CallPlanner.ScoreSatisfaction(Active("Old sat", daysSinceContact: 120, usage: Usage(1, 5), lastSatDays: 200), Today).Should().NotBeNull("past the 180-day window");
        CallPlanner.ScoreSatisfaction(Active("Just talked", daysSinceContact: 45, usage: Usage(1, 5)), Today).Should().BeNull("healthy: 60-day cooldown");
        CallPlanner.ScoreSatisfaction(Active("No usage data", daysSinceContact: 90), Today).Should().NotBeNull("RezervAl-style account without usage: recency alone qualifies");
        CallPlanner.ScoreSatisfaction(Active("Demo", daysSinceContact: 120, status: CustomerStatus.Demo), Today).Should().BeNull();
        CallPlanner.ScoreSatisfaction(Active("Passive", daysSinceContact: 120, status: CustomerStatus.Passive), Today).Should().BeNull();
    }

    [Fact]
    public void Usage_gaps_need_a_full_month_and_maintenance_alone_keeps_the_60_day_rule()
    {
        // Ayın başında herkesin "bu ay" sayısı sıfırdır: geçen ay bakım varsa boşluk yok.
        var earlyMonth = CallPlanner.ScoreSatisfaction(Active("Early", daysSinceContact: 120, usage: Usage(loginDaysAgo: 2, maintenance: 0, prevMaintenance: 7)), Today)!.Value;
        earlyMonth.Reason.Should().NotContain("bakım").And.NotContain("arıza");

        // Önceki ay bilinmiyorsa (yeni firma / anlık görüntü yok) da boşluk sayılmaz.
        CallPlanner.ScoreSatisfaction(Active("Unknown prev", daysSinceContact: 120, usage: Usage(2, 0)), Today)!.Value.Reason.Should().NotContain("bakım");

        // Kapanmış bir ayın anlık görüntüsü tek başına yeter.
        var closedMonth = Usage(2, 0) with { SnapshotMonth = 9 };
        CallPlanner.UsageGaps(closedMonth, Today).NoMaintenance.Should().BeTrue();
        CallPlanner.ScoreSatisfaction(Active("Closed month", daysSinceContact: 120, usage: closedMonth), Today)!.Value.Reason.Should().Contain("bir aydır bakım girişi yok");

        // Bakım boşluğu tek başına 14 günlük kapıyı AÇMAZ: 60 gün temas kuralı ve 180 gün tekrar kuralı yerinde kalır.
        var gapOnly = Usage(loginDaysAgo: 2, maintenance: 0, prevMaintenance: 0, prevFaults: 0);
        CallPlanner.ScoreSatisfaction(Active("Gap, talked 20d ago", daysSinceContact: 20, usage: gapOnly), Today).Should().BeNull();
        CallPlanner.ScoreSatisfaction(Active("Gap, recent sat", daysSinceContact: 120, usage: gapOnly, lastSatDays: 100), Today).Should().BeNull();
        var gap = CallPlanner.ScoreSatisfaction(Active("Gap, due", daysSinceContact: 120, usage: gapOnly), Today)!.Value;
        gap.Reason.Should().Contain("bir aydır bakım girişi yok").And.Contain("120 gün görüşülmedi");

        // Giriş yok GÜÇLÜ sinyaldir: 14 gün sonra girer.
        CallPlanner.ScoreSatisfaction(Active("No login, 20d", daysSinceContact: 20, usage: Usage(40, 5)), Today).Should().NotBeNull();
    }

    [Fact]
    public void Long_silent_healthy_customer_outranks_a_recently_contacted_one_with_a_single_risk_signal()
    {
        var old = CallPlanner.ScoreSatisfaction(Active("Old", daysSinceContact: 300, usage: Usage(2, 12)), Today)!.Value;
        var riskNew = CallPlanner.ScoreSatisfaction(Active("RiskNew", daysSinceContact: 20, usage: Usage(40, 5), lastSatDays: 50), Today)!.Value;
        old.Reason.Should().Contain("300 gün görüşülmedi");
        old.Score.Should().BeGreaterThan(riskNew.Score, "09.11.2026: a firm called 20 days ago must not jump ahead of one silent for 300 days on risk points alone");

        var mid = CallPlanner.ScoreSatisfaction(Active("Mid", daysSinceContact: 100, usage: Usage(2, 12)), Today)!.Value;
        mid.Reason.Should().Contain("100 gün görüşülmedi");
        old.Score.Should().BeGreaterThan(mid.Score, ">180 days earns more than >90");
    }

    [Fact]
    public void Plan_fills_daily_quotas_without_repeating_a_company_and_respects_existing_slots()
    {
        var leads = Enumerable.Range(0, 12).Select(i => Lead($"Lead{i}", CustomerLabel.Potansiyel)).ToList();
        var warm = Enumerable.Range(0, 7).Select(i => Lead($"Warm{i}", CustomerLabel.Potansiyel, daysSinceContact: 60 + i, contacts: 1)).ToList();
        var happy = Enumerable.Range(0, 3).Select(i => Active($"Happy{i}", daysSinceContact: 120, usage: Usage(1, 10))).ToList();
        var all = leads.Concat(warm).Concat(happy).ToList();
        var days = CallPlanner.BusinessDays(Today, 2);
        var existing = new Dictionary<(DateOnly, AppointmentKind), int> { [(Today, AppointmentKind.NewCall)] = 2 };
        var alreadyPlanned = new HashSet<Guid> { leads[0].CustomerId };

        var plan = CallPlanner.Plan(all, days, new CallPlanQuotas(5, 5, 2), new CallPlanSlots(), alreadyPlanned, existing, Today);

        plan.Select(p => p.CustomerId).Should().OnlyHaveUniqueItems("a company is called at most once in the horizon");
        plan.Should().NotContain(p => p.CustomerId == leads[0].CustomerId, "already on the calendar");
        plan.Count(p => p.Day == Today && p.Kind == AppointmentKind.NewCall).Should().Be(3, "2 already planned today");
        plan.Count(p => p.Day == days[1] && p.Kind == AppointmentKind.NewCall).Should().Be(5);
        plan.Count(p => p.Day == Today && p.Kind == AppointmentKind.FollowUpCall).Should().Be(5);
        plan.Count(p => p.Day == days[1] && p.Kind == AppointmentKind.FollowUpCall).Should().Be(2, "only 7 reached leads");
        plan.Count(p => p.Kind == AppointmentKind.SatisfactionCall).Should().Be(3, "3 healthy customers, quota 2+2");
        plan.Where(p => p.Kind == AppointmentKind.NewCall).Should().OnlyContain(p => leads.Any(l => l.CustomerId == p.CustomerId));
        plan.Where(p => p.Kind == AppointmentKind.FollowUpCall).Should().OnlyContain(p => warm.Any(l => l.CustomerId == p.CustomerId));
        plan.Where(p => p.Day == Today && p.Kind == AppointmentKind.NewCall).Select(p => p.Time).Should().Equal(new TimeOnly(10, 30), new TimeOnly(10, 45), new TimeOnly(11, 0));
        plan.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Reason));
    }

    [Fact]
    public void Plan_puts_promised_callbacks_on_their_day_above_quota_always_as_follow_ups()
    {
        var day2 = Today.AddDays(1);
        var promised = Active("Promised", daysSinceContact: 1, next: day2.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc));
        var overdueLead = Lead("Overdue lead", daysSinceContact: 10, contacts: 1, next: Now.AddDays(-3));
        var filler = Enumerable.Range(0, 10).Select(i => Lead($"F{i}", CustomerLabel.Potansiyel, daysSinceContact: 60 + i, contacts: 1)).ToList();

        var plan = CallPlanner.Plan(filler.Concat(new[] { promised, overdueLead }).ToList(), new[] { Today, day2 }, new CallPlanQuotas(0, 5, 0), new CallPlanSlots(),
            new HashSet<Guid>(), new Dictionary<(DateOnly, AppointmentKind), int>(), Today);

        plan.Should().ContainSingle(p => p.CustomerId == promised.CustomerId).Which.Day.Should().Be(day2);
        plan.Should().ContainSingle(p => p.CustomerId == overdueLead.CustomerId).Which.Day.Should().Be(Today, "past-due promise lands on the first day");
        plan.Where(p => p.CustomerId == promised.CustomerId || p.CustomerId == overdueLead.CustomerId)
            .Should().OnlyContain(p => p.Score == 1000 && p.Kind == AppointmentKind.FollowUpCall && p.Reason.StartsWith("Söz verilen takip"));
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
