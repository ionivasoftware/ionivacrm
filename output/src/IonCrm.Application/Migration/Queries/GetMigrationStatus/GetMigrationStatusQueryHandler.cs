using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Migration.Queries.GetMigrationStatus;

/// <summary>Handles <see cref="GetMigrationStatusQuery"/>.</summary>
public sealed class GetMigrationStatusQueryHandler
    : IRequestHandler<GetMigrationStatusQuery, Result<MigrationStatusDto>>
{
    private readonly IMigrationService _migrationService;

    /// <summary>Initialises a new instance of <see cref="GetMigrationStatusQueryHandler"/>.</summary>
    public GetMigrationStatusQueryHandler(IMigrationService migrationService)
    {
        _migrationService = migrationService;
    }

    /// <inheritdoc />
    public Task<Result<MigrationStatusDto>> Handle(
        GetMigrationStatusQuery request,
        CancellationToken cancellationToken)
        => Task.FromResult(Result<MigrationStatusDto>.Success(_migrationService.GetStatus()));
}
