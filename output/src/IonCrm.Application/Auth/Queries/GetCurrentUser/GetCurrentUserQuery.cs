using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Queries.GetCurrentUser;

/// <summary>
/// Query to retrieve the full profile of the currently authenticated user,
/// including their project-role assignments.
/// The user ID is resolved from <see cref="ICurrentUserService"/> inside the handler.
/// </summary>
public record GetCurrentUserQuery : IRequest<Result<AuthResponseDto>>;
