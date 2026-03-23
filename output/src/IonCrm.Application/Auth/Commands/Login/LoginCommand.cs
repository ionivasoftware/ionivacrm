using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.Login;

/// <summary>
/// Command to authenticate a user with email + password credentials.
/// Returns an <see cref="AuthResponseDto"/> containing the JWT access token,
/// refresh token, and user details on success.
/// </summary>
/// <param name="Email">The user's registered email address.</param>
/// <param name="Password">The plain-text password (never stored or logged).</param>
public record LoginCommand(string Email, string Password) : IRequest<Result<AuthResponseDto>>;
