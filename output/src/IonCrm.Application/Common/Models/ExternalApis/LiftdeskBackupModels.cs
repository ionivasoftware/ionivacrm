using System.Text.Json;

namespace IonCrm.Application.Common.Models.ExternalApis;

/// <summary>
/// One backup-pipeline run reported by Liftdesk (docs/crm-backup-api.md §5).
///
/// NOT tenant-scoped: there is a single infrastructure-wide backup covering the whole Liftdesk
/// installation, so this must never be surfaced on a customer card as "that firm's backup".
/// All timestamps are UTC and must be converted for display.
/// </summary>
public record LiftdeskBackupRun(
    Guid Id,
    /// <summary>Backup | Verify | Mirror.</summary>
    string Kind,
    /// <summary>Running | Succeeded | Failed.</summary>
    string Status,
    string? BackupName,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationSeconds,
    /// <summary>Encrypted archive size in bytes (~1.2 GB). Convert with 1024³.</summary>
    long? SizeBytes,
    /// <summary>Source database size in bytes (~8.6 GB) — growth tracking.</summary>
    long? DatabaseSizeBytes,
    int? ArchiveEntries,
    string? Destination,
    /// <summary>
    /// Row counts captured at dump time. Deliberately kept as raw JSON: the contract states the
    /// field set is NOT fixed, so unknown keys must survive and missing ones must not throw.
    /// </summary>
    JsonElement? SourceCounts,
    /// <summary>full (data included) | schema (schema only — the weak state).</summary>
    string? VerifyMode,
    /// <summary>Whether restored row counts matched the dump manifest.</summary>
    bool? CountsMatched,
    string? Message,
    /// <summary>GitHub Actions run link — the "Logu aç" button target.</summary>
    string? RunUrl,
    string? TriggeredBy);

/// <summary>
/// One row of the infrastructure usage/cost breakdown (docs/crm-backup-api.md §8).
///
/// DİKKAT — alanların anlamı karışmasın: <see cref="AvgVCpu"/>, <see cref="AvgRamGb"/>,
/// <see cref="AvgDiskGb"/> ve <see cref="AvgBackupGb"/> pencere boyunca ORTALAMA kullanımdır
/// (toplam değil), <see cref="EgressGb"/> ise pencere boyunca TOPLAM giden trafiktir.
/// </summary>
public record LiftdeskInfraUsageRow(
    string Environment,
    string Service,
    double AvgVCpu,
    double AvgRamGb,
    double AvgDiskGb,
    double AvgBackupGb,
    double EgressGb,
    /// <summary>Bu pencerenin TAHMİNİ maliyeti — kesin fatura değil.</summary>
    decimal EstimatedCostUsd,
    /// <summary>Aynı hızda devam ederse aylık projeksiyon (trend içindir).</summary>
    decimal EstimatedMonthlyUsd);

/// <summary>Per-environment cost subtotal.</summary>
public record LiftdeskEnvironmentTotal(
    string Environment,
    decimal EstimatedCostUsd,
    decimal EstimatedMonthlyUsd);

/// <summary>
/// Infrastructure usage/cost payload from GET /api/v1/crm/backups/infra-usage.
///
/// <see cref="Configured"/> = false BİR HATA DEĞİLDİR: Railway token'ı tanımsızsa ya da API'ye
/// ulaşılamadıysa böyle döner ve <see cref="Message"/> sebebi taşır. Ekranda nötr bir
/// "yapılandırılmadı" olarak gösterilmeli, kırmızı alarm olarak değil.
///
/// <see cref="Rows"/> kaynakta zaten ortam adına, sonra maliyete göre sıralıdır — CRM olduğu gibi basar.
/// </summary>
public record LiftdeskInfraUsage(
    bool Configured,
    string? Message,
    DateTime? PeriodStartUtc,
    DateTime? PeriodEndUtc,
    double? PeriodDays,
    List<LiftdeskInfraUsageRow>? Rows,
    List<LiftdeskEnvironmentTotal>? EnvironmentTotals,
    decimal? TotalEstimatedCostUsd,
    decimal? TotalEstimatedMonthlyUsd,
    DateTime? FetchedAtUtc);

/// <summary>
/// Dashboard-card payload from GET /api/v1/crm/backups/status (docs/crm-backup-api.md §3).
///
/// <see cref="IsHealthy"/> is the single field an operator reads; when false,
/// <see cref="Problems"/> carries operator-facing Turkish reasons that can be rendered verbatim.
///
/// IMPORTANT: silence is not success. No records at all yields IsHealthy=false — the CRM must not
/// treat an empty payload as "nothing to show"; that is precisely the case worth catching.
/// </summary>
public record LiftdeskBackupStatus(
    bool IsHealthy,
    List<string>? Problems,
    LiftdeskBackupRun? LastBackup,
    LiftdeskBackupRun? LastSuccessfulBackup,
    double? HoursSinceLastSuccessfulBackup,
    LiftdeskBackupRun? LastVerify,
    LiftdeskBackupRun? LastSuccessfulVerify,
    double? HoursSinceLastSuccessfulVerify,
    LiftdeskBackupRun? LastMirror,
    int FailuresLast7Days,
    long? LatestBackupSizeBytes,
    long? LatestDatabaseSizeBytes);
