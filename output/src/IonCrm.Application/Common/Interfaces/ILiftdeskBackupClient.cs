using IonCrm.Application.Common.Models.ExternalApis;

namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Read-only M2M client for the Liftdesk (EMS) backup-status API (docs/crm-backup-api.md).
/// Answers "are Liftdesk backups running, and are they actually restorable?".
///
/// Auth is the static Bearer key <c>Liftdesk:ApiKey</c> (CRM__APIKEY — shared with error-triage,
/// tickets and support chat), NOT the per-project LiftdeskSaas key. The key never leaves the server.
///
/// The backup archives themselves are never exposed to the CRM — they contain every tenant's
/// personal data and plaintext passwords. Only the run manifest is read.
/// </summary>
public interface ILiftdeskBackupClient
{
    /// <summary>True when Liftdesk:ApiKey is configured; the backup surfaces 400 cleanly otherwise.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Dashboard-card status. GET /api/v1/crm/backups/status
    /// </summary>
    Task<LiftdeskEnvelope<LiftdeskBackupStatus>> GetStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run history, newest first. GET /api/v1/crm/backups?kind=&amp;limit=
    /// </summary>
    /// <param name="kind">Backup | Verify | Mirror. Null/empty returns every kind; an invalid value 400s upstream.</param>
    /// <param name="limit">Clamped to 1–200 by the source (default 30).</param>
    Task<LiftdeskEnvelope<List<LiftdeskBackupRun>>> GetRunsAsync(
        string? kind,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Infrastructure usage/cost breakdown by environment + service.
    /// GET /api/v1/crm/backups/infra-usage?days=
    /// </summary>
    /// <param name="days">Son N gün (1–90). Null ise kaynak ay başından bugüne hesaplar.</param>
    Task<LiftdeskEnvelope<LiftdeskInfraUsage>> GetInfraUsageAsync(
        int? days,
        CancellationToken cancellationToken = default);
}
