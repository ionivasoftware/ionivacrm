using IonCrm.Application.Common.DTOs;
using IonCrm.Application.Common.Models;
using MediatR;

namespace IonCrm.Application.Auth.Queries.GetUsers;

/// <summary>
/// Returns a list of users.
/// SuperAdmin: can see all users (or filter by project).
/// Other roles: can only see users within their own project.
/// </summary>
/// <param name="ProjectId">
/// Optional project filter. Non-SuperAdmin callers are automatically scoped to their own projects.
/// </param>
public record GetUsersQuery(Guid? ProjectId = null) : IRequest<Result<List<UserDto>>>;
