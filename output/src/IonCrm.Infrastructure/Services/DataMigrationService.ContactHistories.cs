using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>Contact-history migration phase of the data migration service.</summary>
public sealed partial class DataMigrationService
{
    /// <summary>
    /// Migrates contact records from both legacy interview tables.
    /// Phase 3 — dbo.CustomerInterviews  → ContactHistories (LegacyId = "CI-{ID}")
    /// Phase 4 — dbo.AppointedInterviews → ContactHistories (LegacyId = "AI-{ID}",
    ///            historical appointments only: Date &lt; GETDATE())
    /// </summary>
    private async Task MigrateContactHistoriesAsync(
        Guid projectId,
        string connStr,
        Dictionary<string, Guid> customerMap,
        Dictionary<int, int> potentialToCompanyMap,
        CancellationToken ct)
    {
        await MigrateCustomerInterviewsAsync(
            projectId, connStr, customerMap, potentialToCompanyMap, ct);

        await MigrateAppointedInterviewsAsync(
            projectId, connStr, customerMap, potentialToCompanyMap, ct);
    }

    // ── Phase 3: dbo.CustomerInterviews ──────────────────────────────────────

    private async Task MigrateCustomerInterviewsAsync(
        Guid projectId,
        string connStr,
        Dictionary<string, Guid> customerMap,
        Dictionary<int, int> potentialToCompanyMap,
        CancellationToken ct)
    {
        _currentStatus.CurrentOperation = "Counting dbo.CustomerInterviews...";
        int total = await CountMssqlAsync(connStr, "SELECT COUNT(*) FROM dbo.CustomerInterviews", ct);
        _currentStatus.TotalContactHistories += total;

        _logger.LogInformation("dbo.CustomerInterviews: {Total} rows to process.", total);

        int offset = 0;
        while (offset < total)
        {
            _currentStatus.CurrentOperation =
                $"Migrating dbo.CustomerInterviews ({offset}/{total})...";

            const string sql = """
                SELECT ID, UserId, Date, Description, Status, CustomerId,
                       isPotantialCustomer, Type, ProductDescription,
                       ContactPersonName, ContactPersonNumber, CreatedOn
                FROM dbo.CustomerInterviews
                ORDER BY ID
                OFFSET @offset ROWS FETCH NEXT @batchSize ROWS ONLY
                """;

            var rows = await ReadMssqlRowsAsync(connStr, sql, ct,
                ("@offset", offset), ("@batchSize", BatchSize));

            if (rows.Count == 0) break;

            await SaveContactHistoryBatchAsync(
                rows,
                row => $"CI-{row["ID"]}",
                (row, legacyId) =>
                {
                    var customerId = ResolveCustomerId(
                        row["CustomerId"],
                        ReadBit(row.TryGetValue("isPotantialCustomer", out var ip) ? ip : null),
                        customerMap,
                        potentialToCompanyMap);

                    if (customerId == Guid.Empty) return null; // unresolvable → skip

                    var contactedAt = ToUtc(row.TryGetValue("Date", out var dt) ? dt as DateTime? : null);
                    var createdOn   = row.TryGetValue("CreatedOn", out var co) && co is DateTime coDate
                                          ? DateTime.SpecifyKind(coDate, DateTimeKind.Utc)
                                          : contactedAt;

                    return new ContactHistory
                    {
                        Id            = Guid.NewGuid(),
                        CustomerId    = customerId,
                        ProjectId     = projectId,
                        LegacyId      = legacyId,
                        Type          = MapContactType(row.TryGetValue("Type", out var t) ? t?.ToString() : null),
                        Subject       = Truncate(row.TryGetValue("ProductDescription", out var pd) ? pd?.ToString() : null, 300),
                        Content       = BuildInterviewContent(row),
                        Outcome       = Truncate(row.TryGetValue("Status", out var s) ? s?.ToString() : null, 500),
                        ContactedAt   = contactedAt,
                        CreatedAt     = createdOn,
                        UpdatedAt     = DateTime.UtcNow
                    };
                },
                ct);

            offset += rows.Count;
        }

        _currentStatus.CurrentOperation =
            $"dbo.CustomerInterviews done ({_currentStatus.MigratedContactHistories} inserted).";
    }

    // ── Phase 4: dbo.AppointedInterviews (historical) ────────────────────────

    private async Task MigrateAppointedInterviewsAsync(
        Guid projectId,
        string connStr,
        Dictionary<string, Guid> customerMap,
        Dictionary<int, int> potentialToCompanyMap,
        CancellationToken ct)
    {
        _currentStatus.CurrentOperation = "Counting dbo.AppointedInterviews (historical)...";
        const string countSql =
            "SELECT COUNT(*) FROM dbo.AppointedInterviews WHERE Date < GETDATE()";
        int total = await CountMssqlAsync(connStr, countSql, ct);
        _currentStatus.TotalContactHistories += total;

        _logger.LogInformation("dbo.AppointedInterviews (past): {Total} rows to process.", total);

        int offset = 0;
        while (offset < total)
        {
            _currentStatus.CurrentOperation =
                $"Migrating dbo.AppointedInterviews ({offset}/{total})...";

            const string sql = """
                SELECT ID, UserId, Date, Note, Type, Status, CustomerId, isPotentialCustomer
                FROM dbo.AppointedInterviews
                WHERE Date < GETDATE()
                ORDER BY ID
                OFFSET @offset ROWS FETCH NEXT @batchSize ROWS ONLY
                """;

            var rows = await ReadMssqlRowsAsync(connStr, sql, ct,
                ("@offset", offset), ("@batchSize", BatchSize));

            if (rows.Count == 0) break;

            await SaveContactHistoryBatchAsync(
                rows,
                row => $"AI-{row["ID"]}",
                (row, legacyId) =>
                {
                    var customerId = ResolveCustomerId(
                        row["CustomerId"],
                        ReadBit(row.TryGetValue("isPotentialCustomer", out var ip) ? ip : null),
                        customerMap,
                        potentialToCompanyMap);

                    if (customerId == Guid.Empty) return null;

                    var date = ToUtc(row.TryGetValue("Date", out var dt) ? dt as DateTime? : null);

                    return new ContactHistory
                    {
                        Id          = Guid.NewGuid(),
                        CustomerId  = customerId,
                        ProjectId   = projectId,
                        LegacyId    = legacyId,
                        Type        = ContactType.Meeting,
                        Subject     = Truncate("Scheduled appointment (migrated)", 300),
                        Content     = Truncate(row.TryGetValue("Note", out var n) ? n?.ToString() : null, 4000),
                        Outcome     = Truncate(row.TryGetValue("Status", out var s) ? s?.ToString() : null, 500),
                        ContactedAt = date,
                        CreatedAt   = date,
                        UpdatedAt   = DateTime.UtcNow
                    };
                },
                ct);

            offset += rows.Count;
        }

        _currentStatus.CurrentOperation =
            $"dbo.AppointedInterviews done. Total ContactHistories inserted: {_currentStatus.MigratedContactHistories}.";
    }

    // ── Batch persistence helper ──────────────────────────────────────────────

    /// <summary>
    /// Checks a batch of rows for existing LegacyIds then inserts only new
    /// <see cref="ContactHistory"/> entities. Rows where <paramref name="buildEntity"/>
    /// returns null are counted as skipped (unresolvable customer).
    /// </summary>
    private async Task SaveContactHistoryBatchAsync(
        List<Dictionary<string, object?>> rows,
        Func<Dictionary<string, object?>, string> getLegacyId,
        Func<Dictionary<string, object?>, string, ContactHistory?> buildEntity,
        CancellationToken ct)
    {
        var batchIds = rows.Select(getLegacyId).ToList();

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existingIds = (await ctx.ContactHistories
            .IgnoreQueryFilters()
            .Where(h => batchIds.Contains(h.LegacyId!))
            .Select(h => h.LegacyId!)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var legacyId = getLegacyId(row);
            try
            {
                if (existingIds.Contains(legacyId))
                {
                    _currentStatus.SkippedContactHistories++;
                    continue;
                }

                var entity = buildEntity(row, legacyId);
                if (entity is null)
                {
                    _currentStatus.Errors.Add(
                        $"{legacyId}: Could not resolve customer reference — row skipped.");
                    _currentStatus.SkippedContactHistories++;
                    continue;
                }

                ctx.ContactHistories.Add(entity);
                _currentStatus.MigratedContactHistories++;
            }
            catch (Exception ex)
            {
                var msg = $"ContactHistory {legacyId}: {ex.Message}";
                _currentStatus.Errors.Add(msg);
                _logger.LogWarning("Migration row error: {Error}", msg);
                _currentStatus.SkippedContactHistories++;
            }
        }

        await ctx.SaveChangesAsync(ct);
    }

    // ── Customer ID resolution ────────────────────────────────────────────────

    /// <summary>
    /// Resolves the new <see cref="Guid"/> Customer ID from a legacy contact record.
    ///
    /// Resolution logic:
    ///   isPotential = false  → look up "EMS-{CustomerId}"
    ///   isPotential = true   → look up "PC-{CustomerId}";
    ///                          if converted (in potentialToCompanyMap) look up "EMS-{companyId}"
    /// Returns <see cref="Guid.Empty"/> when no matching customer is found.
    /// </summary>
    private static Guid ResolveCustomerId(
        object? rawCustomerIdObj,
        bool isPotential,
        Dictionary<string, Guid> customerMap,
        Dictionary<int, int> potentialToCompanyMap)
    {
        if (rawCustomerIdObj is null) return Guid.Empty;

        int rawId;
        try { rawId = Convert.ToInt32(rawCustomerIdObj); }
        catch { return Guid.Empty; }

        if (!isPotential)
        {
            customerMap.TryGetValue($"EMS-{rawId}", out var id);
            return id;
        }

        // PotentialCustomer that was converted → redirect to EMS company
        if (potentialToCompanyMap.TryGetValue(rawId, out int companyId))
        {
            customerMap.TryGetValue($"EMS-{companyId}", out var emsId);
            return emsId;
        }

        // Standalone potential customer
        customerMap.TryGetValue($"PC-{rawId}", out var pcId);
        return pcId;
    }

    // ── DateTime helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Ensures a nullable <see cref="DateTime"/> has <see cref="DateTimeKind.Utc"/>.
    /// Falls back to <see cref="DateTime.UtcNow"/> when null.
    /// </summary>
    private static DateTime ToUtc(DateTime? dt)
        => dt.HasValue
            ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;
}
