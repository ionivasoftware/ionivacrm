using IonCrm.API.Common;
using IonCrm.Application.Auth.Commands.Login;
using IonCrm.Application.Auth.Commands.Logout;
using IonCrm.Application.Auth.Commands.RefreshToken;
using IonCrm.Application.Auth.Commands.RegisterUser;
using IonCrm.Application.Auth.Commands.UpdateProfile;
using IonCrm.Application.Auth.Queries.GetCurrentUser;
using IonCrm.Application.Common.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Login with email and password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail(result.Errors, 401));
        return Ok(ApiResponse<AuthResponseDto>.Ok(result.Value!));
    }

    /// <summary>Register a new user (SuperAdmin only).</summary>
    [HttpPost("register")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Register([FromBody] RegisterUserCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<UserDto>.Fail(result.Errors));
        return Ok(ApiResponse<UserDto>.Created(result.Value!));
    }

    /// <summary>Refresh access token using refresh token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail("Refresh token not found", 401));
        var command = new RefreshTokenCommand(refreshToken);
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail(result.Errors, 401));
        return Ok(ApiResponse<AuthResponseDto>.Ok(result.Value!));
    }

    /// <summary>Logout and revoke refresh token.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<object>.Ok(new { message = "Logged out successfully" }));
    }

    /// <summary>Get current authenticated user.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var result = await _mediator.Send(new GetCurrentUserQuery());
        if (result.IsFailure)
            return Unauthorized(ApiResponse<UserDto>.Fail(result.Errors, 401));
        return Ok(ApiResponse<UserDto>.Ok(result.Value!));
    }

    /// <summary>Update the current user's profile (name + optional password).</summary>
    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Errors));
        return Ok(ApiResponse<object>.Ok(new { message = "Profil güncellendi." }));
    }
}
