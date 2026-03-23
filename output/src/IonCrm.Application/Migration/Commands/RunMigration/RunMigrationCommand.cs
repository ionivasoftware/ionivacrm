using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Migration.Commands.RunMigration;

/// <summary>
/// Command to trigger the one-time data migration from the legacy MSSQL CRM database.
/// SuperAdmin only. Returns immediately with the initial job status snapshot.
/// Poll GET /api/v1/migration/status for live progress.
/// </summary>
/// <param name="ProjectId">
/// The target project that all migrated customers and contact histories will belong to.
/// </param>
/// <param name="MssqlConnectionString">
/// Live MSSQL connection string for the legacy CRM database. NEVER logged.
/// </param>
public record RunMigrationCommand(
    Guid ProjectId,
    string MssqlConnectionString
) : IRequest<Result<MigrationStatusDto>>;
