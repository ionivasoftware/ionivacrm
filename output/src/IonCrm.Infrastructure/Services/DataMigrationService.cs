using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IMigrationService"/> — orchestrates the one-time migration
/// from the legacy MSSQL CRM (crm.bak) to the new ION CRM PostgreSQL schema.
///
/// Migration phases (in order):
///   Phase 1 — EMS.dbo.Companies        → Customers (Status = Active, LegacyId = "EMS-{ID}")
///   Phase 2 — dbo.PotentialCustomers   → Customers (Status = Lead,   LegacyId = "PC-{ID}",
///                                         only rows where CustomerId IS NULL)
///   Phase 3 — dbo.CustomerInterviews   → ContactHistories (LegacyId = "CI-{ID}")
///   Phase 4 — dbo.AppointedInterviews  → ContactHistories (LegacyId = "AI-{ID}",
///                                         only past appointments)
///
/// Idempotency: before inserting each row the LegacyId is checked;
/// existing records are counted as Skipped instead of re-inserted.
///
/// Registered as a Singleton — state persists across HTTP requests.
/// </summary>
public sealed partial class DataMigrationService : IMigrationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataMigrationService> _logger;

    // ── Thread-safety ─────────────────────────────────────────────────────────
    // SemaphoreSlim(1,1) prevents concurrent migrations.
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    // Volatile reference swap is atomic on all .NET platforms.
    private volatile MigrationStatusDto _currentStatus = new();

    /// <summary>Batch size for reading legacy DB rows and writing to PostgreSQL.</summary>
    private const int BatchSize = 100;

    /// <summary>Initialises a new instance of <see cref="DataMigrationService"/>.</summary>
    public DataMigrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<DataMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ── IMigrationService ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public MigrationStatusDto GetStatus() => _currentStatus;

    /// <inheritdoc />
    public async Task<bool> StartAsync(
        Guid projectId,
        string mssqlConnectionString,
        CancellationToken cancellationToken = default)
    {
        // Reject if already running
        if (_currentStatus.State == MigrationState.Running)
            return false;

        // Try acquiring the semaphore non-blocking
        if (!await _semaphore.WaitAsync(0, cancellationToken))
            return false;

        // Reset status for fresh run
        _currentStatus = new MigrationStatusDto
        {
            State = MigrationState.Running,
            TargetProjectId = projectId,
            StartedAt = DateTime.UtcNow,
            CurrentOperation = "Initialising migration..."
        };

        _logger.LogInformation(
            "Data migration started for project {ProjectId}", projectId);

        // Fire-and-forget — caller polls GetStatus() for progress
        _ = Task.Run(async () =>
        {
            try
            {
                await RunMigrationAsync(projectId, mssqlConnectionString, CancellationToken.None);

                _currentStatus.State = MigrationState.Completed;
                _currentStatus.CompletedAt = DateTime.UtcNow;
                _currentStatus.CurrentOperation = "Migration completed successfully.";

                _logger.LogInformation(
                    "Migration completed. Customers inserted={C} skipped={SC}. " +
                    "ContactHistories inserted={H} skipped={SH}. Errors={E}.",
                    _currentStatus.MigratedCustomers,
                    _currentStatus.SkippedCustomers,
                    _currentStatus.MigratedContactHistories,
                    _currentStatus.SkippedContactHistories,
                    _currentStatus.Errors.Count);
            }
            catch (Exception ex)
            {
                _currentStatus.State = MigrationState.Failed;
                _currentStatus.CompletedAt = DateTime.UtcNow;
                _currentStatus.CurrentOperation = "Migration failed. Review errors for details.";
                _currentStatus.Errors.Add($"Fatal: {ex.Message}");

                // Never log the connection string — ex.Message may contain it indirectly
                // so we only log type + first 200 chars of message.
                var safeMessage = ex.Message.Length > 200
                    ? ex.Message[..200]
                    : ex.Message;
                _logger.LogError(
                    "Migration job failed ({ExType}): {Message}",
                    ex.GetType().Name, safeMessage);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        return true;
    }

    // ── Orchestration ─────────────────────────────────────────────────────────

    private async Task RunMigrationAsync(
        Guid projectId,
        string mssqlConnectionString,
        CancellationToken ct)
    {
        // Fast-fail: verify connectivity before starting long migration
        _currentStatus.CurrentOperation = "Verifying MSSQL connection...";
        await VerifyMssqlConnectionAsync(mssqlConnectionString, ct);

        // Phase 1 + 2: migrate customers from both legacy tables
        await MigrateCustomersAsync(projectId, mssqlConnectionString, ct);

        // Build in-memory lookup maps for contact-history phase
        _currentStatus.CurrentOperation = "Building customer ID maps...";
        var customerMap = await BuildCustomerLegacyMapAsync(projectId, ct);
        var potentialToCompanyMap = await BuildPotentialToCompanyMapAsync(mssqlConnectionString, ct);

        // Phase 3 + 4: migrate contact histories
        await MigrateContactHistoriesAsync(
            projectId, mssqlConnectionString, customerMap, potentialToCompanyMap, ct);
    }

    // ── MSSQL helpers ─────────────────────────────────────────────────────────

    /// <summary>Opens and immediately closes an MSSQL connection to verify the string is valid.</summary>
    private static async Task VerifyMssqlConnectionAsync(string connStr, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
    }

    /// <summary>Executes a scalar COUNT query against the MSSQL database.</summary>
    private static async Task<int> CountMssqlAsync(
        string connStr,
        string sql,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Reads rows from MSSQL into a list of column-value dictionaries.
    /// DBNull values are stored as <see langword="null"/>.
    /// </summary>
    private static async Task<List<Dictionary<string, object?>>> ReadMssqlRowsAsync(
        string connStr,
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        var rows = new List<Dictionary<string, object?>>();

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);

        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    // ── Mapping utilities (static) ────────────────────────────────────────────

    /// <summary>Truncates a string to <paramref name="maxLength"/> characters, or returns null.</summary>
    private static string? Truncate(string? value, int maxLength)
        => value?.Length > maxLength ? value[..maxLength] : value;

    /// <summary>
    /// Reads the object as a bool — handles SQL Server <c>bit</c> as both
    /// <see cref="bool"/> and integer representations.
    /// </summary>
    private static bool ReadBit(object? value)
        => value switch
        {
            bool b => b,
            int i => i != 0,
            byte by => by != 0,
            _ => false
        };

    /// <summary>
    /// Converts a legacy contact type string to the new <see cref="Domain.Enums.ContactType"/> enum.
    /// Covers both Turkish and English type names found in the original DB.
    /// </summary>
    private static Domain.Enums.ContactType MapContactType(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "phone" or "call" or "telefon" or "arama" => Domain.Enums.ContactType.Call,
            "visit" or "ziyaret" => Domain.Enums.ContactType.Visit,
            "meeting" or "toplanti" or "toplantı" or "görüşme" => Domain.Enums.ContactType.Meeting,
            "email" or "e-mail" or "mail" => Domain.Enums.ContactType.Email,
            "whatsapp" => Domain.Enums.ContactType.WhatsApp,
            _ => Domain.Enums.ContactType.Note
        };

    /// <summary>
    /// Builds the Content field for a ContactHistory from CustomerInterviews row data.
    /// Concatenates description + contact person details.
    /// </summary>
    private static string? BuildInterviewContent(Dictionary<string, object?> row)
    {
        var parts = new List<string>(3);

        if (row.TryGetValue("Description", out var desc) && desc?.ToString() is { Length: > 0 } d)
            parts.Add(d);

        var person = row.TryGetValue("ContactPersonName", out var p) ? p?.ToString() : null;
        var phone  = row.TryGetValue("ContactPersonNumber", out var ph) ? ph?.ToString() : null;

        if (!string.IsNullOrWhiteSpace(person) || !string.IsNullOrWhiteSpace(phone))
            parts.Add($"Contact person: {person} {phone}".Trim());

        return Truncate(string.Join(" | ", parts), 4000);
    }
}
