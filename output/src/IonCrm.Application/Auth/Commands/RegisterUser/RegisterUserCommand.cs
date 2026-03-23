using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Commands.RegisterUser;

/// <summary>
/// Command to register a new system user. Restricted to SuperAdmin callers only
/// (authorization enforced in the API controller via [Authorize(Policy = "SuperAdmin")]).
/// </summary>
public record RegisterUserCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    bool IsSuperAdmin = false) : IRequest<Result<UserDto>>;
