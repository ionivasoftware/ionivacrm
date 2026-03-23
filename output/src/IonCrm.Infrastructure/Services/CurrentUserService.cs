using IonCrm.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// Resolves the current user's identity from JWT claims via <see cref="IHttpContextAccessor"/>.
/// Claim names match what <see cref="ITokenService"/> places in the access token.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Initialises a new instance of <see cref="CurrentUserService"/>.</summary>
    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public Guid UserId
    {
        get
        {
            var claim = User?.FindFirstValue("userId") ?? User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }

    /// <inheritdoc />
    public string Email => User?.FindFirstValue("email") ?? User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    /// <inheritdoc />
    public bool IsSuperAdmin
    {
        get
        {
            var claim = User?.FindFirstValue("isSuperAdmin");
            return bool.TryParse(claim, out var val) && val;
        }
    }

    /// <inheritdoc />
    public List<Guid> ProjectIds
    {
        get
        {
            var claim = User?.FindFirstValue("projectIds");
            if (string.IsNullOrWhiteSpace(claim))
                return new List<Guid>();

            return claim
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public string? GetRoleForProject(Guid projectId)
    {
        var claim = User?.FindFirstValue("roles");
        if (string.IsNullOrWhiteSpace(claim))
            return null;

        try
        {
            var roles = JsonSerializer.Deserialize<Dictionary<string, string>>(claim);
            return roles?.TryGetValue(projectId.ToString(), out var role) == true ? role : null;
        }
        catch
        {
            return null;
        }
    }
}
