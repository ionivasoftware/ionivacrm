using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Migration.Queries.GetMigrationStatus;

/// <summary>
/// Query that returns the current state and progress of the data migration job.
/// Safe to poll at any frequency — reads in-memory state only, no DB round-trip.
/// </summary>
public record GetMigrationStatusQuery : IRequest<Result<MigrationStatusDto>>;
