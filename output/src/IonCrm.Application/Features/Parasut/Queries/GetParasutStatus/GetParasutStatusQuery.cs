using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Features.Parasut.Queries.GetParasutStatus;

/// <summary>
/// Returns the Paraşüt connection status.
/// Pass <c>ProjectId = null</c> to query the global connection directly.
/// Pass a specific project id to query that project (falls back to global if no per-project conn).
/// </summary>
public record GetParasutStatusQuery(Guid? ProjectId) : IRequest<Result<ParasutStatusDto>>;

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
