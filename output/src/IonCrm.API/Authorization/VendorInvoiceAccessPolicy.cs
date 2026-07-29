using System.Security.Claims;

namespace IonCrm.API.Authorization;

/// <summary>
/// Authorisation rule behind the "VendorInvoiceAccess" policy: SuperAdmin, or anyone holding the
/// Accounting (muhasebe) role in ANY project.
///
/// Lives in its own class rather than as an inline lambda so it can be unit-tested against a
/// <see cref="ClaimsPrincipal"/> built the way the JWT pipeline really builds one. The original
/// inline version read <c>FindFirst("roles")</c>, which silently returned null in production because
/// JwtBearer's inbound claim mapping rewrites <c>roles</c> to <see cref="ClaimTypes.Role"/> — locking
/// every Accounting user out with a 403.
/// </summary>
public static class VendorInvoiceAccessPolicy
{
    /// <summary>Policy name registered in <c>Program.cs</c> and referenced by <c>[Authorize]</c>.</summary>
    public const string Name = "VendorInvoiceAccess";

    /// <summary>
    /// Claim types the roles dictionary can arrive under. <c>roles</c> is what
    /// <c>TokenService</c> mints (and what survives when <c>MapInboundClaims = false</c>);
    /// <see cref="ClaimTypes.Role"/> is what inbound claim mapping renames it to — accepted so tokens
    /// issued before that setting was turned off keep working until they expire.
    /// </summary>
    private static readonly string[] RoleClaimTypes = ["roles", ClaimTypes.Role];

    /// <summary>
    /// The role value granting finance access. Matched as a quoted JSON value (<c>"Accounting"</c>)
    /// inside the <c>{ projectId: "RoleName" }</c> dictionary, so a project GUID can never collide
    /// with it.
    /// </summary>
    private const string QuotedAccountingRole = "\"Accounting\"";

    /// <summary>Returns true when <paramref name="user"/> may read and manage vendor invoices.</summary>
    public static bool IsSatisfiedBy(ClaimsPrincipal? user)
    {
        if (user is null) return false;

        if (user.HasClaim("isSuperAdmin", "true"))
            return true;

        return user.Claims.Any(c =>
            RoleClaimTypes.Contains(c.Type, StringComparer.Ordinal)
            && c.Value.Contains(QuotedAccountingRole, StringComparison.OrdinalIgnoreCase));
    }
}
