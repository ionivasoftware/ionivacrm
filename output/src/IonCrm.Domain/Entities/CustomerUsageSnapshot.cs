using IonCrm.Domain.Common;

namespace IonCrm.Domain.Entities;

/// <summary>
/// Immutable monthly usage snapshot for one Liftdesk customer — exactly one row per
/// (<see cref="CustomerId"/>, <see cref="SnapshotYear"/>, <see cref="SnapshotMonth"/>).
///
/// WHY this table exists: the churn dashboard must answer "usage is declining vs the previous
/// months", but the live CRM DB cannot — customer <c>Status</c> and elevator counts are recomputed
/// and OVERWRITTEN every 15-minute sync, so no history of a trend survives. This table freezes the
/// usage figures month by month so trends become computable. Populated by
/// <c>UsageSnapshotService</c> from the Liftdesk summary + plan endpoints.
///
/// Two fields depend on Liftdesk-side work not yet shipped: <see cref="LastLoginAt"/> (needs
/// <c>User.LastLoginAt</c>) stays null, and <see cref="WorkOrderCount"/> stays 0, until Liftdesk
/// exposes them — the column exists now so no migration is needed when they arrive.
/// </summary>
public class CustomerUsageSnapshot : BaseEntity
{
    /// <summary>Target project (the Liftdesk project the customer belongs to). Denormalized for tenant scoping.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>The customer this snapshot describes.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Calendar year the monthly activity counts belong to.</summary>
    public int SnapshotYear { get; set; }

    /// <summary>Calendar month (1–12) the monthly activity counts belong to.</summary>
    public int SnapshotMonth { get; set; }

    // ── Point-in-time totals (as of capture; not month-scoped) ───────────────

    /// <summary>Total elevators under the firm at capture time (size denominator — never a health metric alone).</summary>
    public int ElevatorCount { get; set; }

    /// <summary>Total active staff users at capture time.</summary>
    public int UserCount { get; set; }

    /// <summary>Most recent login of any company staff (UTC). Null until Liftdesk exposes it (recency pillar).</summary>
    public DateTime? LastLoginAt { get; set; }

    // ── Monthly activity counts (for SnapshotYear / SnapshotMonth) ───────────

    /// <summary>
    /// Maintenance records this month (the adoption heartbeat).
    ///
    /// ANLAM KIRILMASI: Liftdesk bu sayacı başlangıçta PLANLANAN bakımı sayacak şekilde
    /// gönderiyordu — bakım planı asansör başına otomatik üretildiği için değer fiilen asansör
    /// sayısını yansıtıyor, kullanımı değil. Düzeltmeden sonra TAMAMLANAN bakım sayılıyor.
    /// Snapshot yalnız içinde bulunulan ayı güncellediği için düzeltmeden ÖNCEKİ ay satırları eski
    /// anlamı taşımaya devam eder; geçmişle kıyaslama yapılırken bu kırılma unutulmamalı.
    /// </summary>
    public int MaintenanceCount { get; set; }

    /// <summary>Fault/breakdown records this month.</summary>
    public int FaultCount { get; set; }

    /// <summary>Part-change offers this month (kept separate from revision/assembly for breadth scoring).</summary>
    public int PartChangeOfferCount { get; set; }

    /// <summary>Revision offers this month.</summary>
    public int RevisionOfferCount { get; set; }

    /// <summary>Assembly offers this month.</summary>
    public int AssemblyOfferCount { get; set; }

    /// <summary>Work orders opened this month.</summary>
    public int WorkOrderCount { get; set; }

    /// <summary>
    /// Invoices issued this month — the "fatura" half of the cari-fatura (accounting) usage signal.
    /// Stays 0 until Liftdesk exposes the count.
    /// </summary>
    public int InvoiceCount { get; set; }

    /// <summary>
    /// Collections (tahsilat) recorded this month — the "cari" half of the accounting usage signal.
    /// This is the meaningful one for firms running in CurrentAccount mode, which may never issue an
    /// invoice. Stays 0 until Liftdesk exposes the count.
    /// </summary>
    public int CollectionCount { get; set; }

    // ── Commercial context (weight/priority, not a score input) ──────────────

    /// <summary>Current plan tier name at capture (e.g. Standart / Pro / Prime).</summary>
    public string? PlanTier { get; set; }

    /// <summary>Current plan status at capture (e.g. Active / Trialing / PendingPayment).</summary>
    public string? PlanStatus { get; set; }

    /// <summary>Monthly price of the current plan at capture, if resolvable (revenue-at-risk weight).</summary>
    public decimal? PlanMonthlyPrice { get; set; }

    /// <summary>Customer expiration date at capture (drives Active/Churned derivation).</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>When this snapshot row was actually captured (UTC) — distinct from the month it describes.</summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
