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
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Security.Claims;

namespace IonCrm.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    // Cookie name used for the HttpOnly refresh-token cookie.
    private const string RefreshTokenCookieName = "refreshToken";

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

        var dto = result.Value!;

        // SECURITY FIX (CRITICAL-001):
        // The refresh token must NEVER appear in the JSON response body — it would be
        // readable by any JavaScript on the page (XSS risk). Deliver it exclusively via
        // an HttpOnly, Secure, SameSite=Strict cookie so the browser manages it
        // transparently and JS cannot access it.
        SetRefreshTokenCookie(dto.RefreshToken);
        dto.RefreshToken = string.Empty;   // strip from body

        return Ok(ApiResponse<AuthResponseDto>.Ok(dto));
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

    /// <summary>
    /// Refresh access token using the HttpOnly refresh-token cookie.
    /// No request body is required — the browser sends the cookie automatically.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail("Refresh token not found", 401));

        var command = new RefreshTokenCommand(refreshToken);
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            // Revoked or expired — clear the stale cookie
            Response.Cookies.Delete(RefreshTokenCookieName);
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail(result.Errors, 401));
        }

        var dto = result.Value!;

        // Rotate: set the newly issued refresh token as a cookie and strip from body
        SetRefreshTokenCookie(dto.RefreshToken);
        dto.RefreshToken = string.Empty;

        return Ok(ApiResponse<AuthResponseDto>.Ok(dto));
    }

    /// <summary>
    /// Logout the current user by revoking the refresh token stored in the HttpOnly cookie.
    /// Pass <c>{ "logoutEverywhere": true }</c> to revoke all sessions.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutOptionsRequest? options = null)
    {
        // Read the refresh token from the HttpOnly cookie (not from the request body)
        var refreshToken = Request.Cookies[RefreshTokenCookieName] ?? string.Empty;

        var userIdClaim = User.FindFirstValue("userId");
        Guid.TryParse(userIdClaim, out var userId);

        var command = new LogoutCommand(
            RefreshToken: refreshToken,
            UserId: userId,
            LogoutEverywhere: options?.LogoutEverywhere ?? false);

        var result = await _mediator.Send(command);

        // Always clear the cookie — even if the handler reports an error
        Response.Cookies.Delete(RefreshTokenCookieName);

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

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Appends a secure HttpOnly cookie containing <paramref name="rawToken"/>.
    /// The cookie is invisible to JavaScript and is transmitted only over HTTPS.
    /// </summary>
    private void SetRefreshTokenCookie(string rawToken)
    {
        Response.Cookies.Append(RefreshTokenCookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,                 // Not accessible via document.cookie
            Secure   = true,                 // HTTPS only
            SameSite = SameSiteMode.Strict,  // No cross-site request inclusion
            Expires  = DateTimeOffset.UtcNow.AddDays(7)
        });
    }
}

/// <summary>
/// Optional request body for <c>POST /api/v1/auth/logout</c>.
/// Send <c>{ "logoutEverywhere": true }</c> to revoke all sessions for this user.
/// </summary>
public sealed record LogoutOptionsRequest(bool LogoutEverywhere = false);
