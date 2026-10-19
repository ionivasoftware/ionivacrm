using IonCrm.Domain.Enums;

namespace IonCrm.Application.Features.CallPlan;

/// <summary>Latest monthly usage snapshot of a Liftdesk customer (null for RezervAl / unknown).</summary>
public record UsageSignal(
    DateTime? LastLoginAt,
    int MaintenanceCount,
    int FaultCount,
    int WorkOrderCount,
    int InvoiceCount,
    int CollectionCount,
    int ElevatorCount,
    int UserCount,
    string? PlanStatus,
    int SnapshotYear,
    int SnapshotMonth);

/// <summary>Everything the planner knows about one company. Built by the data source, scored here.</summary>
public record PlanCandidate(
    Guid CustomerId,
    string CompanyName,
    CustomerStatus Status,
    CustomerLabel? Label,
    DateTime CreatedAt,
    DateTime? LastContactAt,
    int ContactCount,
    DateTime? LastSatisfactionCallAt,
    /// <summary>Promised callback from the most recent contact record ("şu tarihte tekrar arayın").</summary>
    DateTime? NextContactAt,
    DateTime? ExpirationDate,
    bool IsInternal,
    Guid? AssignedUserId,
    UsageSignal? Usage);

public record CallPlanQuotas(int NewCalls = 5, int FollowUps = 5, int Satisfaction = 2);

/// <summary>Local slot start hours (TR) per kind; calls are spaced 15 minutes apart within a kind.</summary>
public record CallPlanSlots(int NewCallHour = 10, int FollowUpHour = 14, int SatisfactionHour = 16, int SatisfactionMinute = 30, int StepMinutes = 15);

public record PlannedCall(Guid CustomerId, AppointmentKind Kind, DateOnly Day, TimeOnly Time, int Score, string Reason, Guid? AssignedUserId);

/// <summary>
/// The CSO planner — pure rules, no I/O, unit-tested. Per business day it fills three quotas
/// (new / follow-up / satisfaction) from ranked candidates; a company appears at most once in the
/// horizon; promised callbacks (NextContactAt) are honoured on their day even above quota. Every
/// pick carries a Turkish reason so the operator sees why this company, today.
/// </summary>
public static class CallPlanner
{
    public const int NewCallCooldownDays = 7;
    public const int FollowUpCooldownDays = 14;
    public const int SatisfactionCooldownDays = 30;
    public const int SatisfactionRepeatDays = 90;

    // ── Business days ────────────────────────────────────────────────────────

    /// <summary>Next <paramref name="count"/> business days (Mon–Fri) starting at <paramref name="start"/> inclusive.</summary>
    public static List<DateOnly> BusinessDays(DateOnly start, int count)
    {
        var days = new List<DateOnly>();
        var d = start;
        while (days.Count < count)
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(d);
            d = d.AddDays(1);
        }
        return days;
    }

    /// <summary>Planning starts today until 17:00 local; after that, tomorrow.</summary>
    public static DateOnly StartDay(DateTime nowLocal) =>
        nowLocal.Hour >= 17 ? DateOnly.FromDateTime(nowLocal).AddDays(1) : DateOnly.FromDateTime(nowLocal);

    // ── Scoring ──────────────────────────────────────────────────────────────

    public static (int Score, string Reason)? ScoreNewCall(PlanCandidate c, DateOnly today, DateOnly horizonEnd)
    {
        if (c.IsInternal || c.Status != CustomerStatus.Lead) return null;
        var since = DaysSince(c.LastContactAt, today);
        if (since is < NewCallCooldownDays) return null;
        if (c.NextContactAt is { } next && DateOnly.FromDateTime(next) > horizonEnd) return null; // "sonra arayın" dedi

        var parts = new List<string> { "Lead" };
        var score = c.Label switch
        {
            CustomerLabel.YuksekPotansiyel => 40, CustomerLabel.Potansiyel => 30, CustomerLabel.Notr => 15,
            CustomerLabel.Vasat => 5, CustomerLabel.Kotu => 0, _ => 12,
        };
        if (c.Label is { } l) parts.Add(LabelText(l));

        if (since is null) { score += 30; parts.Add("hiç görüşülmedi"); }
        else if (since > 60) { score += 25; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 30) { score += 20; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 14) { score += 10; parts.Add($"{since} gün görüşülmedi"); }

        var age = today.DayNumber - DateOnly.FromDateTime(c.CreatedAt).DayNumber;
        if (age <= 14) { score += 20; parts.Add("yeni kayıt"); }
        if (c.Label == CustomerLabel.Kotu && c.ContactCount >= 5) { score -= 30; parts.Add("çok denendi"); }

        return (score, string.Join(" · ", parts));
    }

    public static (int Score, string Reason)? ScoreFollowUp(PlanCandidate c, DateOnly today)
    {
        if (c.IsInternal) return null;
        var recentlyChurned = c.Status == CustomerStatus.Churned && c.ExpirationDate is { } exp && (today.DayNumber - DateOnly.FromDateTime(exp).DayNumber) <= 90;
        if (c.Status is not (CustomerStatus.Active or CustomerStatus.Demo or CustomerStatus.Passive) && !recentlyChurned) return null;

        var since = DaysSince(c.LastContactAt, today);
        var promised = c.NextContactAt is { } n && DateOnly.FromDateTime(n) <= today;
        if (since is < FollowUpCooldownDays && !promised) return null;

        var score = 0; var signals = 0; var parts = new List<string>();
        if (promised) { score += 100; signals++; parts.Add("söz verilen takip"); }
        if (c.Status == CustomerStatus.Demo) { score += 30; signals++; parts.Add("demo sürecinde"); }
        if (c.Status == CustomerStatus.Passive) { score += 15; signals++; parts.Add("pasif"); }
        if (recentlyChurned) { score += 25; signals++; parts.Add("yakın zamanda ayrıldı — geri kazanım"); }

        if (c.ExpirationDate is { } e)
        {
            var left = DateOnly.FromDateTime(e).DayNumber - today.DayNumber;
            if (left is >= 0 and <= 30) { score += 40; signals++; parts.Add($"süresi {left} gün içinde doluyor"); }
            else if (left is > 30 and <= 60) { score += 20; signals++; parts.Add($"süresi {left} gün içinde doluyor"); }
        }

        if (c.Usage is { } u)
        {
            var login = DaysSince(u.LastLoginAt, today);
            if (login is null || login > 14) { score += 30; signals++; parts.Add(login is null ? "hiç giriş yok" : $"{login} gün giriş yok"); }
            if (u.MaintenanceCount == 0 && u.ElevatorCount > 0) { score += 25; signals++; parts.Add("bu ay bakım girişi yok"); }
            if (u.PlanStatus is { } ps && (ps.Contains("Trial", StringComparison.OrdinalIgnoreCase) || ps.Contains("Demo", StringComparison.OrdinalIgnoreCase)))
            { score += 15; signals++; parts.Add("deneme planı"); }
            if (signals > 0 && u.FaultCount + u.WorkOrderCount == 0) { score += 10; parts.Add("arıza/iş emri yok"); }

            // Kullanım verisi olan ve sağlıklı görünen hesapta yalnız "uzun süredir görüşülmedi" bir takip
            // gerekçesi değildir — o müşteri memnuniyet aramasının adayıdır.
            if (signals == 0) return null;
        }

        if (since is null) { score += 20; parts.Add("hiç görüşülmedi"); }
        else if (since > 90) { score += 25; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 45) { score += 15; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 30) { score += 5; parts.Add($"{since} gün görüşülmedi"); }

        if (score <= 0) return null;
        parts.Insert(0, StatusText(c.Status));
        return (score, string.Join(" · ", parts));
    }

    public static (int Score, string Reason)? ScoreSatisfaction(PlanCandidate c, DateOnly today)
    {
        if (c.IsInternal || c.Status != CustomerStatus.Active) return null;
        var since = DaysSince(c.LastContactAt, today);
        if (since is < SatisfactionCooldownDays) return null;
        var sinceSat = DaysSince(c.LastSatisfactionCallAt, today);
        if (sinceSat is < SatisfactionRepeatDays) return null;

        // Healthy usage only — an inactive account is a follow-up, not a satisfaction call.
        if (c.Usage is { } u)
        {
            var login = DaysSince(u.LastLoginAt, today);
            var healthy = (login is <= 14) || u.MaintenanceCount > 0;
            if (!healthy) return null;
        }

        var score = 0; var parts = new List<string> { "Aktif müşteri" };
        if (sinceSat is null) { score += 30; parts.Add("hiç memnuniyet araması yapılmadı"); }
        else if (sinceSat > 180) { score += 25; parts.Add($"son memnuniyet araması {sinceSat} gün önce"); }
        else { score += 10; parts.Add($"son memnuniyet araması {sinceSat} gün önce"); }

        if (c.ExpirationDate is { } e)
        {
            var left = DateOnly.FromDateTime(e).DayNumber - today.DayNumber;
            if (left is >= 60 and <= 150) { score += 15; parts.Add("yenileme yaklaşıyor"); }
        }
        if (today.DayNumber - DateOnly.FromDateTime(c.CreatedAt).DayNumber > 90) score += 10;
        if (c.Usage is { MaintenanceCount: >= 10 }) { score += 10; parts.Add("yoğun kullanım"); }
        if (since is { } s && s > 90) parts.Add($"{s} gün görüşülmedi");

        return (score, string.Join(" · ", parts));
    }

    // ── Planning ─────────────────────────────────────────────────────────────

    /// <param name="candidates">All companies of the project.</param>
    /// <param name="days">Business days to fill, in order.</param>
    /// <param name="alreadyPlannedCustomers">Customers with a Planned appointment anywhere in the horizon — never re-planned.</param>
    /// <param name="existingPerDayKind">Planned appointments already on (day, kind) — count against quotas.</param>
    public static List<PlannedCall> Plan(
        IReadOnlyList<PlanCandidate> candidates,
        IReadOnlyList<DateOnly> days,
        CallPlanQuotas quotas,
        CallPlanSlots slots,
        ISet<Guid> alreadyPlannedCustomers,
        IReadOnlyDictionary<(DateOnly Day, AppointmentKind Kind), int> existingPerDayKind,
        DateOnly today)
    {
        var result = new List<PlannedCall>();
        if (days.Count == 0) return result;
        var horizonEnd = days[^1];
        var used = new HashSet<Guid>(alreadyPlannedCustomers);

        var newRanked = Rank(candidates, c => ScoreNewCall(c, today, horizonEnd));
        var followRanked = Rank(candidates, c => ScoreFollowUp(c, today));
        var satRanked = Rank(candidates, c => ScoreSatisfaction(c, today));

        foreach (var day in days)
        {
            // 1) Promised callbacks due on (or before, for the first day) this day — above quota.
            var promisedToday = candidates
                .Where(c => !used.Contains(c.CustomerId) && !c.IsInternal && c.NextContactAt is { } n &&
                            (DateOnly.FromDateTime(n) == day || (day == days[0] && DateOnly.FromDateTime(n) < day)))
                .ToList();
            var followCount = existingPerDayKind.GetValueOrDefault((day, AppointmentKind.FollowUpCall));
            foreach (var c in promisedToday)
            {
                var kind = c.Status == CustomerStatus.Lead ? AppointmentKind.NewCall : AppointmentKind.FollowUpCall;
                var idx = kind == AppointmentKind.FollowUpCall ? followCount++ : existingPerDayKind.GetValueOrDefault((day, AppointmentKind.NewCall));
                result.Add(new PlannedCall(c.CustomerId, kind, day, SlotTime(kind, idx, slots), 1000,
                    $"Söz verilen takip ({c.NextContactAt:dd.MM.yyyy})", c.AssignedUserId));
                used.Add(c.CustomerId);
            }

            Fill(AppointmentKind.NewCall, quotas.NewCalls, newRanked);
            Fill(AppointmentKind.FollowUpCall, quotas.FollowUps, followRanked);
            Fill(AppointmentKind.SatisfactionCall, quotas.Satisfaction, satRanked);

            void Fill(AppointmentKind kind, int quota, List<(PlanCandidate C, int Score, string Reason)> ranked)
            {
                var existing = existingPerDayKind.GetValueOrDefault((day, kind)) + result.Count(r => r.Day == day && r.Kind == kind && r.Score != 1000);
                var slot = existingPerDayKind.GetValueOrDefault((day, kind)) + result.Count(r => r.Day == day && r.Kind == kind);
                var need = quota - existing;
                foreach (var (c, score, reason) in ranked)
                {
                    if (need <= 0) break;
                    if (used.Contains(c.CustomerId)) continue;
                    if (c.NextContactAt is { } n && DateOnly.FromDateTime(n) > day) continue; // sözü verilen gün gelince alınır
                    result.Add(new PlannedCall(c.CustomerId, kind, day, SlotTime(kind, slot++, slots), score, reason, c.AssignedUserId));
                    used.Add(c.CustomerId);
                    need--;
                }
            }
        }
        return result;
    }

    public static TimeOnly SlotTime(AppointmentKind kind, int index, CallPlanSlots slots)
    {
        var (h, m) = kind switch
        {
            AppointmentKind.NewCall => (slots.NewCallHour, 0),
            AppointmentKind.SatisfactionCall => (slots.SatisfactionHour, slots.SatisfactionMinute),
            _ => (slots.FollowUpHour, 0),
        };
        var minutes = Math.Clamp(h * 60 + m + index * slots.StepMinutes, 0, 23 * 60 + 45);
        return new TimeOnly(minutes / 60, minutes % 60);
    }

    private static List<(PlanCandidate C, int Score, string Reason)> Rank(
        IReadOnlyList<PlanCandidate> candidates, Func<PlanCandidate, (int Score, string Reason)?> scorer) =>
        candidates.Select(c => (c, s: scorer(c))).Where(x => x.s is not null)
            .Select(x => (x.c, x.s!.Value.Score, x.s.Value.Reason))
            .OrderByDescending(x => x.Score).ThenBy(x => x.c.LastContactAt ?? DateTime.MinValue).ThenBy(x => x.c.CompanyName)
            .ToList();

    private static int? DaysSince(DateTime? at, DateOnly today) =>
        at is { } d ? today.DayNumber - DateOnly.FromDateTime(d).DayNumber : null;

    public static string LabelText(CustomerLabel l) => l switch
    {
        CustomerLabel.YuksekPotansiyel => "Yüksek potansiyel", CustomerLabel.Potansiyel => "Potansiyel", CustomerLabel.Notr => "Nötr",
        CustomerLabel.Vasat => "Vasat", CustomerLabel.Kotu => "Kötü", _ => l.ToString(),
    };

    public static string StatusText(CustomerStatus s) => s switch
    {
        CustomerStatus.Active => "Aktif müşteri", CustomerStatus.Demo => "Demo", CustomerStatus.Passive => "Pasif", CustomerStatus.Churned => "Ayrılmış", CustomerStatus.Lead => "Lead", _ => s.ToString(),
    };
}
