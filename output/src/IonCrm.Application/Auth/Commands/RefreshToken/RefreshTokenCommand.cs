using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.RefreshToken;

/// <summary>
/// Command to exchange a valid refresh token for a new access + refresh token pair.
/// The old refresh token is revoked (one-time use).
/// </summary>
/// <param name="Token">The raw refresh token previously issued to the client.</param>
public record RefreshTokenCommand(string Token) : IRequest<Result<AuthResponseDto>>;
