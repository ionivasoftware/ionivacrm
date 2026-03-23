using IonCrm.API.Common;
using IonCrm.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IonCrm.API.Controllers;

/// <summary>
/// Base controller providing ApiResponse helpers and MediatR access.
/// All CRM controllers inherit from this class.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    private IMediator? _mediator;

    /// <summary>Gets the MediatR sender (lazy-resolved from DI).</summary>
    protected IMediator Mediator =>
        _mediator ??= HttpContext.RequestServices.GetRequiredService<IMediator>();

    /// <summary>Returns a 200 OK wrapped in <see cref="ApiResponse{T}"/>.</summary>
    protected IActionResult OkResponse<T>(T data, string? message = null)
        => Ok(ApiResponse<T>.Ok(data, message));

    /// <summary>Returns a 201 Created wrapped in <see cref="ApiResponse{T}"/>.</summary>
    protected IActionResult CreatedResponse<T>(string actionName, object routeValues, T data)
        => CreatedAtAction(actionName, routeValues, ApiResponse<T>.Created(data));

    /// <summary>Returns a 404 Not Found wrapped in <see cref="ApiResponse{T}"/>.</summary>
    protected IActionResult NotFoundResponse<T>(string message)
        => NotFound(ApiResponse<T>.Fail(message, 404));

    /// <summary>Returns a 403 Forbidden wrapped in <see cref="ApiResponse{T}"/>.</summary>
    protected IActionResult ForbiddenResponse<T>(string message)
        => StatusCode(403, ApiResponse<T>.Fail(message, 403));

    /// <summary>
    /// Converts a <see cref="Result{T}"/> into the appropriate HTTP response.
    /// Maps common error patterns to 404/403 status codes.
    /// </summary>
    protected IActionResult ResultToResponse<T>(Result<T> result, bool created = false)
    {
        if (result.IsSuccess)
        {
            return created
                ? StatusCode(201, ApiResponse<T>.Created(result.Value!))
                : OkResponse(result.Value!);
        }

        var error = result.FirstError ?? "An error occurred.";

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return NotFoundResponse<T>(error);

        if (error.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            return ForbiddenResponse<T>(error);

        return BadRequest(ApiResponse<T>.Fail(result.Errors));
    }

    /// <summary>
    /// Converts a <see cref="Result"/> (no value) into the appropriate HTTP response.
    /// </summary>
    protected IActionResult ResultToResponse(Result result)
    {
        if (result.IsSuccess)
            return OkResponse<object>(new { }, "Operation completed successfully.");

        var error = result.FirstError ?? "An error occurred.";

        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return NotFound(ApiResponse<object>.Fail(error, 404));

        if (error.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, ApiResponse<object>.Fail(error, 403));

        return BadRequest(ApiResponse<object>.Fail(result.Errors));
    }
}
