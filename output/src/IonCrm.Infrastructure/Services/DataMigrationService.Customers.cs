using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IonCrm.Infrastructure.Services;

/// <summary>Customer-migration phase of the data migration service.</summary>
public sealed partial class DataMigrationService
{
    /// <summary>
    /// Migrates customers from the two legacy customer tables.
    /// Phase 1 — EMS.dbo.Companies     → Customers (Status=Active, LegacyId="EMS-{ID}")
    /// Phase 2 — dbo.PotentialCustomers → Customers (Status=Lead,   LegacyId="PC-{ID}",
    ///            only rows where CustomerId IS NULL — converted ones link to an EMS company)
    /// </summary>
    private async Task MigrateCustomersAsync(
        Guid projectId,
        string connStr,
        CancellationToken ct)
    {
        await MigrateEmsCompaniesAsync(projectId, connStr, ct);
        await MigratePotentialCustomersAsync(projectId, connStr, ct);
    }

    // ── Phase 1: EMS.dbo.Companies ────────────────────────────────────────────

    private async Task MigrateEmsCompaniesAsync(
        Guid projectId,
        string connStr,
        CancellationToken ct)
    {
        _currentStatus.CurrentOperation = "Counting EMS.dbo.Companies...";
        int total = await CountMssqlAsync(connStr, "SELECT COUNT(*) FROM EMS.dbo.Companies", ct);
        _currentStatus.TotalCustomers += total;

        _logger.LogInformation("EMS.dbo.Companies: {Total} rows to process.", total);

        int offset = 0;
        while (offset < total)
        {
            _currentStatus.CurrentOperation =
                $"Migrating EMS.dbo.Companies ({offset}/{total})...";

            const string sql = """
                SELECT ID, Name, Phone, Email, Adress, TaxNumber, TaxUnit
                FROM EMS.dbo.Companies
                ORDER BY ID
                OFFSET @offset ROWS FETCH NEXT @batchSize ROWS ONLY
                """;

            var rows = await ReadMssqlRowsAsync(connStr, sql, ct,
                ("@offset", offset), ("@batchSize", BatchSize));

            if (rows.Count == 0) break;

            await SaveCustomerBatchAsync(
                rows,
                row => $"EMS-{row["ID"]}",
                (row, legacyId) => new Customer
                {
                    Id          = Guid.NewGuid(),
                    ProjectId   = projectId,
                    LegacyId    = legacyId,
                    CompanyName = Truncate(row["Name"]?.ToString() ?? "Unknown", 300)!,
                    Email       = Truncate(row["Email"]?.ToString(), 256),
                    Phone       = Truncate(row["Phone"]?.ToString(), 50),
                    Address     = Truncate(row["Adress"]?.ToString(), 500), // original typo preserved
                    TaxNumber   = Truncate(row["TaxNumber"]?.ToString(), 50),
                    TaxUnit     = Truncate(row["TaxUnit"]?.ToString(), 200),
                    Status      = CustomerStatus.Active,
                    CreatedAt   = DateTime.UtcNow,
                    UpdatedAt   = DateTime.UtcNow
                },
                isCustomer: true,
                ct);

            offset += rows.Count;
        }

        _currentStatus.CurrentOperation =
            $"EMS.dbo.Companies done ({_currentStatus.MigratedCustomers} inserted).";
    }

    // ── Phase 2: dbo.PotentialCustomers ──────────────────────────────────────

    private async Task MigratePotentialCustomersAsync(
        Guid projectId,
        string connStr,
        CancellationToken ct)
    {
        _currentStatus.CurrentOperation = "Counting dbo.PotentialCustomers...";

        // Only import rows that were NOT yet converted to EMS companies
        const string countSql =
            "SELECT COUNT(*) FROM dbo.PotentialCustomers WHERE CustomerId IS NULL";
        int total = await CountMssqlAsync(connStr, countSql, ct);
        _currentStatus.TotalCustomers += total;

        _logger.LogInformation("dbo.PotentialCustomers (unconverted): {Total} rows to process.", total);

        int offset = 0;
        while (offset < total)
        {
            _currentStatus.CurrentOperation =
                $"Migrating dbo.PotentialCustomers ({offset}/{total})...";

            const string sql = """
                SELECT ID, CompanyName, ContactName, Address, Email, Phone
                FROM dbo.PotentialCustomers
                WHERE CustomerId IS NULL
                ORDER BY ID
                OFFSET @offset ROWS FETCH NEXT @batchSize ROWS ONLY
                """;

            var rows = await ReadMssqlRowsAsync(connStr, sql, ct,
                ("@offset", offset), ("@batchSize", BatchSize));

            if (rows.Count == 0) break;

            await SaveCustomerBatchAsync(
                rows,
                row => $"PC-{row["ID"]}",
                (row, legacyId) => new Customer
                {
                    Id          = Guid.NewGuid(),
                    ProjectId   = projectId,
                    LegacyId    = legacyId,
                    CompanyName = Truncate(row["CompanyName"]?.ToString() ?? "Unknown", 300)!,
                    ContactName = Truncate(row["ContactName"]?.ToString(), 200),
                    Email       = Truncate(row["Email"]?.ToString(), 256),
                    Phone       = Truncate(row["Phone"]?.ToString(), 50),
                    Address     = Truncate(row["Address"]?.ToString(), 500),
                    Status      = CustomerStatus.Lead,
                    CreatedAt   = DateTime.UtcNow,
                    UpdatedAt   = DateTime.UtcNow
                },
                isCustomer: true,
                ct);

            offset += rows.Count;
        }

        _currentStatus.CurrentOperation =
            $"dbo.PotentialCustomers done ({_currentStatus.MigratedCustomers} total inserted).";
    }

    // ── Batch persistence helper ──────────────────────────────────────────────

    /// <summary>
    /// Checks a batch of rows for existing LegacyIds (single IN query) then
    /// inserts only the new ones as <see cref="Customer"/> entities.
    /// </summary>
    private async Task SaveCustomerBatchAsync(
        List<Dictionary<string, object?>> rows,
        Func<Dictionary<string, object?>, string> getLegacyId,
        Func<Dictionary<string, object?>, string, Customer> buildEntity,
        bool isCustomer,
        CancellationToken ct)
    {
        // Collect all legacy IDs in this batch
        var batchIds = rows.Select(getLegacyId).ToList();

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // One query per batch — much cheaper than N individual AnyAsync calls
        var existingIds = (await ctx.Customers
            .IgnoreQueryFilters()
            .Where(c => batchIds.Contains(c.LegacyId!))
            .Select(c => c.LegacyId!)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var legacyId = getLegacyId(row);
            try
            {
                if (existingIds.Contains(legacyId))
                {
                    _currentStatus.SkippedCustomers++;
                }
                else
                {
                    ctx.Customers.Add(buildEntity(row, legacyId));
                    _currentStatus.MigratedCustomers++;
                }
            }
            catch (Exception ex)
            {
                var msg = $"Customer {legacyId}: {ex.Message}";
                _currentStatus.Errors.Add(msg);
                _logger.LogWarning("Migration row error: {Error}", msg);
            }
        }

        await ctx.SaveChangesAsync(ct);
    }

    // ── Lookup map builders ───────────────────────────────────────────────────

    /// <summary>
    /// Queries the new CRM database and returns a dict of legacyId → new Customer.Id
    /// for all customers belonging to <paramref name="projectId"/>.
    /// </summary>
    private async Task<Dictionary<string, Guid>> BuildCustomerLegacyMapAsync(
        Guid projectId,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await ctx.Customers
            .IgnoreQueryFilters()
            .Where(c => c.ProjectId == projectId && c.LegacyId != null && !c.IsDeleted)
            .Select(c => new { c.LegacyId, c.Id })
            .ToDictionaryAsync(x => x.LegacyId!, x => x.Id, ct);
    }

    /// <summary>
    /// Reads dbo.PotentialCustomers where CustomerId IS NOT NULL from MSSQL.
    /// Returns a mapping of PC.ID → EMS.Companies.ID for redirecting contact
    /// histories that reference a PotentialCustomer that was later converted.
    /// </summary>
    private static async Task<Dictionary<int, int>> BuildPotentialToCompanyMapAsync(
        string connStr,
        CancellationToken ct)
    {
        const string sql =
            "SELECT ID, CustomerId FROM dbo.PotentialCustomers WHERE CustomerId IS NOT NULL";

        var rows = await ReadMssqlRowsAsync(connStr, sql, ct);

        return rows.ToDictionary(
            r => Convert.ToInt32(r["ID"]),
            r => Convert.ToInt32(r["CustomerId"]));
    }
}
