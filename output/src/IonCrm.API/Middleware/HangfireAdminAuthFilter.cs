using Hangfire.Dashboard;

namespace IonCrm.API.Middleware;

/// <summary>
/// Restricts access to the Hangfire dashboard.
/// Only users with the "isSuperAdmin" JWT claim can access it.
/// In development, access is always allowed for convenience.
/// </summary>
public sealed class HangfireAdminAuthFilter : IDashboardAuthorizationFilter
{
    /// <inheritdoc />
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Allow in development without authentication
        if (httpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>()
            .IsDevelopment())
        {
            return true;
        }

        // Require authenticated SuperAdmin in other environments
        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
            return false;

        return httpContext.User.HasClaim("isSuperAdmin", "true");
    }
}
