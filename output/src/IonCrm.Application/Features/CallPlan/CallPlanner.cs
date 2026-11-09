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
    int SnapshotMonth,
    /// <summary>Previous month's counts when that snapshot exists — lets the planner judge "no maintenance" over a FULL month (null = unknown).</summary>
    int? PrevMaintenanceCount = null,
    int? PrevFaultCount = null,
    int? PrevWorkOrderCount = null);

/// <summary>Everything the planner knows about one company. Built by the data source, scored here.</summary>
/// <remarks>
/// <see cref="LastContactAt"/> and <see cref="ContactCount"/> cover real conversations only (call / meeting /
/// visit / WhatsApp — not e-mails, not internal notes). <see cref="LastContactAt"/> is the last attempt of any
/// outcome (drives cooldowns); <see cref="ContactCount"/> counts attempts where the customer was reached.
/// </remarks>
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
    UsageSignal? Usage,
    /// <summary>E-mail and internal-note records (not conversations). Only for the reason text: "aranmadı (yalnız 4 e-posta/not kaydı)" instead of a bare "hiç görüşülmedi" when the timeline is full of automated mails (09.11.2026, Apap Lifts).</summary>
    int OtherContactCount = 0)
{
    /// <summary>At least one conversation actually took place (reached).</summary>
    public bool WasReached => ContactCount > 0;

    /// <summary>Reason fragment for "no conversation yet" that says what the timeline does hold.</summary>
    public string NeverTalkedText => OtherContactCount > 0
        ? $"aranmadı (yalnız {OtherContactCount} e-posta/not kaydı)"
        : "hiç görüşülmedi";
}

public record CallPlanQuotas(int NewCalls = 5, int FollowUps = 5, int Satisfaction = 2);

/// <summary>Local slot start hours (TR) per kind; calls are spaced 15 minutes apart within a kind.</summary>
public record CallPlanSlots(int NewCallHour = 10, int FollowUpHour = 14, int SatisfactionHour = 16, int SatisfactionMinute = 30, int StepMinutes = 15);

public record PlannedCall(Guid CustomerId, AppointmentKind Kind, DateOnly Day, TimeOnly Time, int Score, string Reason, Guid? AssignedUserId);

/// <summary>
/// The CSO planner — pure rules, no I/O, unit-tested. Per business day it fills three quotas from ranked
/// candidates; a company appears at most once in the horizon; promised callbacks (NextContactAt) are honoured
/// on their day even above quota. Every pick carries a Turkish reason so the operator sees why this company, today.
/// <para>Pools (user decision 02.11.2026):</para>
/// <list type="bullet">
/// <item><b>Yeni arama</b> — leads (and demo accounts) never reached by phone, passive companies (expired trial /
/// dormant) and customers churned in the last 90 days (win-back).</item>
/// <item><b>Takip araması</b> — leads / demo accounts that were reached before: the conversation continues.</item>
/// <item><b>Memnuniyet araması</b> — the only planned call to an Active customer. Risk signals (no login, no
/// maintenance entries, renewal within 60 days) are scored here with a short cooldown; healthy customers are
/// called rarely (60 days since any contact, 180 days since the last satisfaction call).</item>
/// </list>
/// </summary>
public static class CallPlanner
{
    public const int NewCallCooldownDays = 7;
    public const int FollowUpCooldownDays = 14;

    /// <summary>Planlama ufku: bugün + sonraki iş günü (kullanıcı 09.11.2026 — "1 hafta sonrasına değil 2 gün sonrasına"). Günlük koşu ufku bir gün ileri taşır; env <c>CallPlan__HorizonBusinessDays</c> ezer.</summary>
    public const int DefaultHorizonBusinessDays = 2;
    /// <summary>Memnuniyet araması seyrek olmalı (kullanıcı kararı 19.10.2026): son temastan en az 60 gün, son memnuniyet aramasından en az 180 gün.</summary>
    public const int SatisfactionCooldownDays = 60;
    public const int SatisfactionRepeatDays = 180;
    /// <summary>Ayrılmış müşteri bu kadar gün içinde geri kazanım için yeni arama havuzuna girer.</summary>
    public const int WinBackDays = 90;
    /// <summary>Pasif firmada denemenin bitişi bu kadar gün içindeyse sıcak sayılır.</summary>
    public const int FreshExpiryDays = 30;

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

    /// <summary>
    /// Yeni arama: hiç ulaşılmamış Lead/Demo, pasif firmalar ve son 90 günde ayrılmış müşteriler. Aktif müşteri
    /// ve daha önce görüşülmüş lead buraya girmez.
    /// </summary>
    public static (int Score, string Reason)? ScoreNewCall(PlanCandidate c, DateOnly today, DateOnly horizonEnd)
    {
        if (c.IsInternal) return null;
        var recentlyChurned = RecentlyChurned(c, today);
        var eligible = c.Status switch
        {
            CustomerStatus.Lead or CustomerStatus.Demo => !c.WasReached,
            CustomerStatus.Passive => true,
            CustomerStatus.Churned => recentlyChurned,
            _ => false,
        };
        if (!eligible) return null;

        var since = DaysSince(c.LastContactAt, today);
        if (since is < NewCallCooldownDays) return null;
        if (c.NextContactAt is { } next && DateOnly.FromDateTime(next) > horizonEnd) return null; // "sonra arayın" dedi

        var parts = new List<string> { StatusText(c.Status) };
        var score = LabelScore(c.Label);
        if (c.Label is { } l) parts.Add(LabelText(l));

        if (since is null) { score += 30; parts.Add(c.NeverTalkedText); }
        else if (!c.WasReached) { score += 20; parts.Add($"ulaşılamadı, son deneme {since} gün önce"); }
        else if (since > 60) { score += 25; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 30) { score += 20; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 14) { score += 10; parts.Add($"{since} gün görüşülmedi"); }

        if (c.Status == CustomerStatus.Demo) { score += 30; parts.Add("demo sürecinde"); score += ExpiryScore(c, today, parts); }
        if (c.Status == CustomerStatus.Passive)
        {
            score += 15; // StatusText zaten "Pasif" der
            if (c.ExpirationDate is { } e)
            {
                var ago = today.DayNumber - DateOnly.FromDateTime(e).DayNumber;
                if (ago is >= 0 and <= FreshExpiryDays) { score += 20; parts.Add($"denemesi {ago} gün önce bitti"); }
            }
        }
        if (recentlyChurned) { score += 25; parts.Add("yakın zamanda ayrıldı — geri kazanım"); }

        var age = today.DayNumber - DateOnly.FromDateTime(c.CreatedAt).DayNumber;
        if (age <= 14 && c.Status is CustomerStatus.Lead or CustomerStatus.Demo) { score += 20; parts.Add("yeni kayıt"); }
        if (c.Label == CustomerLabel.Kotu && c.ContactCount >= 5) { score -= 30; parts.Add("çok denendi"); }

        return (score, string.Join(" · ", parts));
    }

    /// <summary>
    /// Takip araması: daha önce ulaşılmış Lead/Demo kayıtları — görüşme devam ediyor. Aktif müşteri memnuniyet
    /// aramasından, pasif/ayrılmış firma yeni aramadan yakalanır; buraya girmezler.
    /// </summary>
    public static (int Score, string Reason)? ScoreFollowUp(PlanCandidate c, DateOnly today)
    {
        if (c.IsInternal) return null;
        if (c.Status is not (CustomerStatus.Lead or CustomerStatus.Demo)) return null;
        if (!c.WasReached) return null; // hiç görüşülmemiş → yeni arama

        var since = DaysSince(c.LastContactAt, today);
        var promised = c.NextContactAt is { } n && DateOnly.FromDateTime(n) <= today;
        if (since is < FollowUpCooldownDays && !promised) return null;

        var parts = new List<string>();
        var score = LabelScore(c.Label);
        if (c.Label is { } l) parts.Add(LabelText(l));
        if (promised) { score += 100; parts.Add("söz verilen takip"); }
        if (c.Status == CustomerStatus.Demo) { score += 30; parts.Add("demo sürecinde"); }
        score += ExpiryScore(c, today, parts);

        if (c.Status == CustomerStatus.Demo && c.Usage is { } u)
        {
            var login = DaysSince(u.LastLoginAt, today);
            if (login is null || login > 7) { score += 25; parts.Add(login is null ? "hiç giriş yok" : $"{login} gün giriş yok"); }
            if (u.PlanStatus is { } ps && (ps.Contains("Trial", StringComparison.OrdinalIgnoreCase) || ps.Contains("Demo", StringComparison.OrdinalIgnoreCase)))
            { score += 15; parts.Add("deneme planı"); }
        }

        if (since > 90) { score += 25; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 45) { score += 15; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 30) { score += 5; parts.Add($"{since} gün görüşülmedi"); }
        parts.Add(c.ContactCount == 1 ? "1 görüşme yapıldı" : $"{c.ContactCount} görüşme yapıldı");
        if (c.Label == CustomerLabel.Kotu && c.ContactCount >= 5) { score -= 30; parts.Add("çok denendi"); }

        if (score <= 0) return null;
        parts.Insert(0, StatusText(c.Status));
        return (score, string.Join(" · ", parts));
    }

    /// <summary>
    /// Memnuniyet araması: aktif müşterinin tek planlı araması. GÜÇLÜ sinyal (giriş yok, süre dolumu) 14 gün bekleme
    /// ile girer (riskli müşteri burada yakalanır). Kullanım boşlukları (bakım / arıza girişi yok) yalnız TAM bir ay
    /// gözlenince sayılır (<see cref="UsageGaps"/>) ve puan ekler ama 60 gün / 180 gün kuralını kaldırmaz — ayın 2'sinde
    /// herkesin "bu ay bakımı yok"tu ve 20 gün önce görüşülen firma 300 gündür aranmayanın önüne geçiyordu (09.11.2026).
    /// Uzun süredir görüşülmeyen firma eskilik puanıyla öne çıkar.
    /// </summary>
    public static (int Score, string Reason)? ScoreSatisfaction(PlanCandidate c, DateOnly today)
    {
        if (c.IsInternal || c.Status != CustomerStatus.Active) return null;
        var since = DaysSince(c.LastContactAt, today);
        var sinceSat = DaysSince(c.LastSatisfactionCallAt, today);

        var strong = new List<string>();   // kısa bekleme süresini açar
        var soft = new List<string>();     // yalnız puan
        var riskScore = 0;
        if (c.Usage is { } u)
        {
            var login = DaysSince(u.LastLoginAt, today);
            if (login is null || login > 14) { riskScore += 30; strong.Add(login is null ? "hiç giriş yok" : $"{login} gün giriş yok"); }
            var (noMaintenance, noFaults) = UsageGaps(u, today);
            if (noMaintenance && u.ElevatorCount > 0) { riskScore += 25; soft.Add("bir aydır bakım girişi yok"); }
            if ((strong.Count > 0 || noMaintenance) && noFaults) { riskScore += 10; soft.Add("bir aydır arıza/iş emri yok"); }
        }
        riskScore += ExpiryScore(c, today, strong);
        var atRisk = strong.Count > 0;

        if (atRisk) { if (since is < FollowUpCooldownDays) return null; }
        else
        {
            if (since is < SatisfactionCooldownDays) return null;
            if (sinceSat is < SatisfactionRepeatDays) return null;
        }

        var score = riskScore;
        var parts = new List<string> { "Aktif müşteri" };
        parts.AddRange(strong);
        parts.AddRange(soft);
        if (sinceSat is null) { score += 30; parts.Add("hiç memnuniyet araması yapılmadı"); }
        else if (sinceSat > 365) { score += 25; parts.Add($"son memnuniyet araması {sinceSat} gün önce"); }
        else { score += 10; parts.Add($"son memnuniyet araması {sinceSat} gün önce"); }

        if (!atRisk && c.ExpirationDate is { } e)
        {
            var left = DateOnly.FromDateTime(e).DayNumber - today.DayNumber;
            if (left is > 60 and <= 150) { score += 15; parts.Add("yenileme yaklaşıyor"); }
        }
        if (today.DayNumber - DateOnly.FromDateTime(c.CreatedAt).DayNumber > 90) score += 10;
        if (c.Usage is { MaintenanceCount: >= 10 }) { score += 10; parts.Add("yoğun kullanım"); }

        // Eskilik puanı: uzun süredir görüşülmeyen firma, yalnız risk puanıyla gelen yakın zamanda görüşülmüşün önüne geçer.
        if (since is null) { score += 20; parts.Add(c.NeverTalkedText); }
        else if (since > 180) { score += 20; parts.Add($"{since} gün görüşülmedi"); }
        else if (since > 90) { score += 10; parts.Add($"{since} gün görüşülmedi"); }

        return (score, string.Join(" · ", parts));
    }

    /// <summary>
    /// Kullanım boşlukları yalnız TAM bir ay gözlenince sayılır: anlık görüntü kapanmış bir aya aitse o ayın sıfırı
    /// yeter; içinde bulunduğumuz ayınsa (ayın 2'sinde herkes sıfırdır) bir önceki ay da bilinmeli ve sıfır olmalı.
    /// </summary>
    public static (bool NoMaintenance, bool NoFaultsOrWorkOrders) UsageGaps(UsageSignal u, DateOnly today)
    {
        var isCurrentMonth = u.SnapshotYear == today.Year && u.SnapshotMonth == today.Month;
        bool Gap(int current, int? previous) => current == 0 && (!isCurrentMonth || previous == 0);
        int? prevFaults = u.PrevFaultCount is null || u.PrevWorkOrderCount is null ? null : u.PrevFaultCount + u.PrevWorkOrderCount;
        return (Gap(u.MaintenanceCount, u.PrevMaintenanceCount), Gap(u.FaultCount + u.WorkOrderCount, prevFaults));
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
            // 1) Promised callbacks due on (or before, for the first day) this day — above quota. A promise follows a
            //    conversation, so it is always a follow-up call whatever the company's status.
            var promisedToday = candidates
                .Where(c => !used.Contains(c.CustomerId) && !c.IsInternal && c.NextContactAt is { } n &&
                            (DateOnly.FromDateTime(n) == day || (day == days[0] && DateOnly.FromDateTime(n) < day)))
                .ToList();
            var followCount = existingPerDayKind.GetValueOrDefault((day, AppointmentKind.FollowUpCall));
            foreach (var c in promisedToday)
            {
                result.Add(new PlannedCall(c.CustomerId, AppointmentKind.FollowUpCall, day, SlotTime(AppointmentKind.FollowUpCall, followCount++, slots), 1000,
                    $"Söz verilen takip ({c.NextContactAt:dd.MM.yyyy}) · {StatusText(c.Status)}", c.AssignedUserId));
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<(PlanCandidate C, int Score, string Reason)> Rank(
        IReadOnlyList<PlanCandidate> candidates, Func<PlanCandidate, (int Score, string Reason)?> scorer) =>
        candidates.Select(c => (c, s: scorer(c))).Where(x => x.s is not null)
            .Select(x => (x.c, x.s!.Value.Score, x.s.Value.Reason))
            .OrderByDescending(x => x.Score).ThenBy(x => x.c.LastContactAt ?? DateTime.MinValue).ThenBy(x => x.c.CompanyName)
            .ToList();

    private static int? DaysSince(DateTime? at, DateOnly today) =>
        at is { } d ? today.DayNumber - DateOnly.FromDateTime(d).DayNumber : null;

    private static bool RecentlyChurned(PlanCandidate c, DateOnly today) =>
        c.Status == CustomerStatus.Churned && c.ExpirationDate is { } exp && (today.DayNumber - DateOnly.FromDateTime(exp).DayNumber) is >= 0 and <= WinBackDays;

    /// <summary>Süre dolumu yaklaşıyorsa puan ekler ve gerekçeyi yazar (≤30 gün: +40, ≤60 gün: +20).</summary>
    private static int ExpiryScore(PlanCandidate c, DateOnly today, List<string> parts)
    {
        if (c.ExpirationDate is not { } e) return 0;
        var left = DateOnly.FromDateTime(e).DayNumber - today.DayNumber;
        if (left is >= 0 and <= 30) { parts.Add($"süresi {left} gün içinde doluyor"); return 40; }
        if (left is > 30 and <= 60) { parts.Add($"süresi {left} gün içinde doluyor"); return 20; }
        return 0;
    }

    public static int LabelScore(CustomerLabel? label) => label switch
    {
        CustomerLabel.YuksekPotansiyel => 40, CustomerLabel.Potansiyel => 30, CustomerLabel.Notr => 15,
        CustomerLabel.Vasat => 5, CustomerLabel.Kotu => 0, _ => 12,
    };

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
