using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IonCrm.API.Authorization;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace IonCrm.Tests.Auth;

/// <summary>
/// Regression tests for <see cref="VendorInvoiceAccessPolicy"/> — the rule behind the
/// "VendorInvoiceAccess" policy that guards the Gelen Faturalar (vendor invoices) screen.
///
/// Why these exist: the original rule read <c>FindFirst("roles")</c>. That works against a
/// hand-built ClaimsPrincipal but NOT in production, because JwtBearer's inbound claim mapping
/// rewrites the <c>roles</c> claim to <see cref="ClaimTypes.Role"/> — so every Accounting user got a
/// 403 while unit tests stayed green. The tests below therefore assert BOTH claim shapes, and one
/// drives a token minted by the real <see cref="TokenService"/> through the real JWT handler.
/// </summary>
public class VendorInvoiceAccessPolicyTests
{
    private const string ProjectId = "9f1d3a4e-0000-4000-8000-000000000001";
    private const string Secret = "ion-crm-test-signing-secret-at-least-32-chars-long!!";

    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));

    private static string RolesJson(string role) => $"{{\"{ProjectId}\":\"{role}\"}}";

    // ── Claim shape: unmapped (MapInboundClaims = false — what the API now configures) ──────────

    [Fact]
    public void Accounting_WithShortRolesClaim_IsAllowed()
    {
        var user = PrincipalWith(
            new Claim("isSuperAdmin", "false"),
            new Claim("roles", RolesJson("Accounting")));

        VendorInvoiceAccessPolicy.IsSatisfiedBy(user).Should().BeTrue();
    }

    // ── Claim shape: mapped (ClaimTypes.Role) — the production shape that caused the 403 ────────

    [Fact]
    public void Accounting_WithMappedRoleClaimType_IsAllowed()
    {
        // This is exactly what JwtBearer produced with MapInboundClaims left at its default.
        // The pre-fix rule returned false here, which is how the whole Accounting role got locked out.
        var user = PrincipalWith(
            new Claim("isSuperAdmin", "false"),
            new Claim(ClaimTypes.Role, RolesJson("Accounting")));

        VendorInvoiceAccessPolicy.IsSatisfiedBy(user).Should().BeTrue();
    }

    // ── SuperAdmin bypass ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void SuperAdmin_IsAllowed_EvenWithoutAnyRole()
    {
        var user = PrincipalWith(new Claim("isSuperAdmin", "true"));

        VendorInvoiceAccessPolicy.IsSatisfiedBy(user).Should().BeTrue();
    }

    // ── Denials ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SalesRep")]
    [InlineData("SalesManager")]
    [InlineData("ProjectAdmin")]
    public void NonAccountingRole_IsDenied(string role)
    {
        var user = PrincipalWith(
            new Claim("isSuperAdmin", "false"),
            new Claim("roles", RolesJson(role)));

        VendorInvoiceAccessPolicy.IsSatisfiedBy(user).Should().BeFalse();
    }

    [Fact]
    public void UserWithNoRoles_IsDenied()
    {
        var user = PrincipalWith(new Claim("isSuperAdmin", "false"), new Claim("roles", "{}"));

        VendorInvoiceAccessPolicy.IsSatisfiedBy(user).Should().BeFalse();
    }

    [Fact]
    public void NullPrincipal_IsDenied()
    {
        VendorInvoiceAccessPolicy.IsSatisfiedBy(null).Should().BeFalse();
    }

    /// <summary>A project GUID must never be mistaken for the role value.</summary>
    [Fact]
    public void ProjectIdContainingRoleWord_DoesNotGrantAccess()
    {
        var user = PrincipalWith(
            new Claim("isSuperAdmin", "false"),
            new Claim("roles", "{\"Accounting-department\":\"SalesRep\"}"));

        VendorInvoiceAccessPolicy.IsSatisfiedBy(user).Should().BeFalse();
    }

    // ── End-to-end: real token → real JWT handler → policy ──────────────────────────────────────

    /// <summary>
    /// Mints a token with the production <see cref="TokenService"/> and validates it through the real
    /// JWT handler under BOTH claim-mapping settings. This is the check a hand-built principal cannot
    /// make: it proves the policy holds against whatever claim type the pipeline actually produces.
    /// </summary>
    [Theory]
    [InlineData(true)]   // inbound mapping ON  → claim arrives as ClaimTypes.Role (the old prod behaviour)
    [InlineData(false)]  // inbound mapping OFF → claim keeps the short "roles" name (current config)
    public void AccountingToken_ThroughRealJwtPipeline_IsAllowed(bool mapInboundClaims)
    {
        var token = GenerateAccessTokenFor(UserRole.Accounting);
        var principal = ValidateToken(token, mapInboundClaims);

        VendorInvoiceAccessPolicy.IsSatisfiedBy(principal).Should().BeTrue(
            "an Accounting user must reach the vendor-invoice endpoints regardless of inbound claim mapping");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonAccountingToken_ThroughRealJwtPipeline_IsDenied(bool mapInboundClaims)
    {
        var token = GenerateAccessTokenFor(UserRole.SalesRep);
        var principal = ValidateToken(token, mapInboundClaims);

        VendorInvoiceAccessPolicy.IsSatisfiedBy(principal).Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string GenerateAccessTokenFor(UserRole role)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"]   = Secret,
                ["JwtSettings:Issuer"]   = "IonCrm",
                ["JwtSettings:Audience"] = "IonCrmUsers",
                ["JwtSettings:AccessTokenExpiryMinutes"] = "15",
            })
            .Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"vendor-invoice-policy-{Guid.NewGuid()}")
            .Options;
        // GenerateAccessToken never touches the DB; the context is only here to satisfy the ctor.
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.IsSuperAdmin).Returns(true);
        currentUser.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        using var db = new ApplicationDbContext(options, currentUser.Object);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "muhasebe@ioniva.test",
            IsSuperAdmin = false,
            UserProjectRoles =
            [
                new UserProjectRole { ProjectId = Guid.Parse(ProjectId), Role = role }
            ],
        };

        var tokenService = new TokenService(config, db, NullLogger<TokenService>.Instance);
        return tokenService.GenerateAccessToken(user);
    }

    private static ClaimsPrincipal ValidateToken(string token, bool mapInboundClaims)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = mapInboundClaims };
        return handler.ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = "IonCrm",
                ValidAudience            = "IonCrmUsers",
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                ClockSkew                = TimeSpan.FromSeconds(30),
            },
            out _);
    }
}
