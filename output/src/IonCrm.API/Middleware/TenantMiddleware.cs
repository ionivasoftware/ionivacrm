using System.Security.Claims;

namespace IonCrm.API.Middleware;

/// <summary>
/// Runs after UseAuthentication() and UseAuthorization() to log the resolved
/// multi-tenant context (UserId, IsSuperAdmin, ProjectIds) for each authenticated request.
///
/// The actual tenant data is resolved on-demand by <c>ICurrentUserService</c> via
/// <c>IHttpContextAccessor</c> — this middleware is the extension point for future
/// synchronous tenant validation (e.g. confirming project IDs still exist in the DB).
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    /// <summary>Initialises a new instance of <see cref="TenantMiddleware"/>.</summary>
    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Processes the HTTP request and logs tenant context for authenticated users.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId     = context.User.FindFirstValue("userId");
            var superAdmin = context.User.FindFirstValue("isSuperAdmin");
            var projectIds = context.User.FindFirstValue("projectIds");

            _logger.LogDebug(
                "Tenant context resolved — UserId={UserId} IsSuperAdmin={IsSuperAdmin} ProjectIds=[{ProjectIds}]",
                userId, superAdmin, projectIds ?? "none");
        }

        await _next(context);
    }
}
