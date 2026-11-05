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
/// <param name="UsesOAuth">Row authorised with the new flow (no password stored).</param>
/// <param name="NeedsReconnect">Refresh rejected for good — a human must re-authorise from Settings.</param>
/// <param name="CanConnectWithOAuth">The new flow is configured on the server ("Paraşüt'e Bağlan" button).</param>
/// <param name="RefreshTokenExpiresAt">When the connection dies if nothing refreshes it (new flow, ~30 days rolling).</param>
public record ParasutStatusDto(
    bool   IsConnected,
    long?  CompanyId,
    string? CompanyName,
    string? Username,
    DateTime? TokenExpiresAt,
    DateTime? LastConnectedAt,
    string? LastError,
    int ReconnectAttempts,
    bool UsesOAuth,
    bool NeedsReconnect,
    bool CanConnectWithOAuth,
    DateTime? RefreshTokenExpiresAt,
    DateTime? ConnectedAt
);
