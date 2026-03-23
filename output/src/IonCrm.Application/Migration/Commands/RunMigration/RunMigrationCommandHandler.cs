using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Migration.Commands.RunMigration;

/// <summary>
/// Handles <see cref="RunMigrationCommand"/>.
/// Delegates to <see cref="IMigrationService.StartAsync"/> (fire-and-forget)
/// and returns the initial status snapshot immediately.
/// </summary>
public sealed class RunMigrationCommandHandler
    : IRequestHandler<RunMigrationCommand, Result<MigrationStatusDto>>
{
    private readonly IMigrationService _migrationService;

    /// <summary>Initialises a new instance of <see cref="RunMigrationCommandHandler"/>.</summary>
    public RunMigrationCommandHandler(IMigrationService migrationService)
    {
        _migrationService = migrationService;
    }

    /// <inheritdoc />
    public async Task<Result<MigrationStatusDto>> Handle(
        RunMigrationCommand request,
        CancellationToken cancellationToken)
    {
        var started = await _migrationService.StartAsync(
            request.ProjectId,
            request.MssqlConnectionString,
            cancellationToken);

        if (!started)
        {
            return Result<MigrationStatusDto>.Failure(
                "A migration job is already running. " +
                "Poll GET /api/v1/migration/status to track progress.");
        }

        return Result<MigrationStatusDto>.Success(_migrationService.GetStatus());
    }
}
