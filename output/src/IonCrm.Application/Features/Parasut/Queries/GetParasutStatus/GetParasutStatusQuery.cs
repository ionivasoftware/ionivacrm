using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutStatus;

/// <summary>Returns the Paraşüt connection status for the given project.</summary>
public record GetParasutStatusQuery(Guid ProjectId) : IRequest<Result<ParasutStatusDto>>;

/// <summary>Paraşüt connection status DTO.</summary>
public record ParasutStatusDto(
    bool   IsConnected,
    long?  CompanyId,
    string? Username,
    DateTime? TokenExpiresAt,
    DateTime? LastConnectedAt,
    string? LastError,
    int ReconnectAttempts
);
