using FluentValidation;
using IonCrm.API.Common;
using System.Text.Json;

namespace IonCrm.API.Middleware;

/// <summary>
/// Catches unhandled exceptions and returns structured <see cref="ApiResponse{T}"/> JSON.
/// Handles FluentValidation exceptions with 400, all others with 500.
/// NEVER logs sensitive data (passwords, tokens).
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    /// <summary>Initialises a new instance of <see cref="GlobalExceptionMiddleware"/>.</summary>
    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Processes the HTTP request and catches any unhandled exceptions.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error on {Path}: {Errors}",
                context.Request.Path,
                string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)));

            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";

            var errors = ex.Errors.Select(e => e.ErrorMessage).ToList();
            var response = ApiResponse<object>.Fail(errors, 400);
            response.Message = "Validation failed.";

            await context.Response.WriteAsync(JsonSerializer.Serialize(response,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            var response = ApiResponse<object>.Fail("An unexpected error occurred. Please try again.", 500);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }
}
