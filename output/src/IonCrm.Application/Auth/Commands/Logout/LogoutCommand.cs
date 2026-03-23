using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.Logout;

/// <summary>
/// Command to log out the current user by revoking their refresh token.
/// Pass <paramref name="LogoutEverywhere"/> as true to revoke ALL sessions for the user.
/// </summary>
/// <param name="RefreshToken">The raw refresh token to revoke.</param>
/// <param name="UserId">The ID of the currently authenticated user (from JWT claims).</param>
/// <param name="LogoutEverywhere">When true, all refresh tokens for the user are revoked.</param>
public record LogoutCommand(
    string RefreshToken,
    Guid UserId,
    bool LogoutEverywhere = false) : IRequest<Result>;
