using IonCrm.API.Common;
using IonCrm.Application.Auth.Commands.AssignRole;
using IonCrm.Application.Auth.Commands.DeleteUser;
using IonCrm.Application.Auth.Commands.RegisterUser;
using IonCrm.Application.Auth.Commands.UpdateUser;
using IonCrm.Application.Auth.Queries.GetUsers;
using IonCrm.Application.Common.DTOs;
using IonCrm.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Manages user accounts and project-role assignments.
/// All endpoints require authentication; role-level restrictions are documented per endpoint.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>Initialises a new instance of <see cref="UsersController"/>.</summary>
    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── GET /api/v1/users ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a list of users.
    /// SuperAdmin sees all users; other roles see only users in their own project.
    /// </summary>
    /// <param name="projectId">Optional project filter (SuperAdmin only can use this freely).</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetUsers([FromQuery] Guid? projectId = null)
    {
        var result = await _mediator.Send(new GetUsersQuery(projectId));
        if (result.IsFailure)
            return BadRequest(ApiResponse<List<UserDto>>.Fail(result.Errors));
        return Ok(ApiResponse<List<UserDto>>.Ok(result.Value!));
    }

    // ── POST /api/v1/users ───────────────────────────────────────────────────

    /// <summary>
    /// Registers a new user. Restricted to SuperAdmin.
    /// Sends the same <see cref="RegisterUserCommand"/> as the auth endpoint.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), 201)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Register([FromBody] RegisterUserCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<UserDto>.Fail(result.Errors));
        return StatusCode(201, ApiResponse<UserDto>.Created(result.Value!));
    }

    // ── PUT /api/v1/users/{id} ───────────────────────────────────────────────

    /// <summary>Updates user details. Restricted to SuperAdmin.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), 400)]
    public async Task<IActionResult> UpdateUser(
        [FromRoute] Guid id,
        [FromBody] UpdateUserCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        if (result.IsFailure)
            return BadRequest(ApiResponse<UserDto>.Fail(result.Errors));
        return Ok(ApiResponse<UserDto>.Ok(result.Value!));
    }

    // ── DELETE /api/v1/users/{id} ────────────────────────────────────────────

    /// <summary>Soft-deletes a user. Restricted to SuperAdmin.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> DeleteUser([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new DeleteUserCommand(id));
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return NoContent();
    }

    // ── POST /api/v1/users/{userId}/roles ────────────────────────────────────

    /// <summary>
    /// Assigns (or updates) a role for a user within a project.
    /// Restricted to SuperAdmin.
    /// </summary>
    /// <param name="userId">The user to assign the role to.</param>
    /// <param name="request">The assignment details: ProjectId and Role.</param>
    [HttpPost("{userId:guid}/roles")]
    [Authorize(Policy = "SuperAdmin")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> AssignRole(
        [FromRoute] Guid userId,
        [FromBody] AssignRoleRequest request)
    {
        var command = new AssignRoleCommand(userId, request.ProjectId, request.Role);
        var result  = await _mediator.Send(command);

        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));

        return Ok(ApiResponse<object>.Ok(
            new { message = $"Role '{request.Role}' assigned to user {userId} in project {request.ProjectId}." }));
    }
}

/// <summary>Request body for the AssignRole endpoint.</summary>
/// <param name="ProjectId">The project (tenant) to assign the role in.</param>
/// <param name="Role">The role to assign.</param>
public record AssignRoleRequest(Guid ProjectId, UserRole Role);
