using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;

namespace IonCrm.Tests.Auth;

/// <summary>
/// Tests focusing on JWT token claims correctness and expiry.
/// Uses the real <see cref="TokenService"/> with an in-memory database.
/// </summary>
public class TokenClaimsTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TokenService _tokenService;

    public TokenClaimsTests()
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(x => x.ProjectIds).Returns(new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options, currentUserMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"]                   = "test-super-secret-signing-key-min-32!!",
                ["JwtSettings:Issuer"]                   = "IonCrmTest",
                ["JwtSettings:Audience"]                 = "IonCrmTestUsers",
                ["JwtSettings:AccessTokenExpiryMinutes"] = "15",
                ["JwtSettings:RefreshTokenExpiryDays"]   = "7"
            })
            .Build();

        var loggerMock = new Mock<ILogger<TokenService>>();
        _tokenService = new TokenService(config, _context, loggerMock.Object);
    }

    public void Dispose() => _context.Dispose();

    // ── Claim correctness ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_ContainsUserId()
    {
        // Arrange
        var user = MakeUser();

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        var jwt = ParseJwt(token);
        jwt.Claims.First(c => c.Type == "userId").Value.Should().Be(user.Id.ToString());
    }

    [Fact]
    public void GenerateAccessToken_NormalUser_IsSuperAdminClaimIsFalse()
    {
        // Arrange
        var user = MakeUser(isSuperAdmin: false);

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        var jwt = ParseJwt(token);
        jwt.Claims.First(c => c.Type == "isSuperAdmin").Value.Should().Be("false");
    }

    [Fact]
    public void GenerateAccessToken_SuperAdmin_IsSuperAdminClaimIsTrue()
    {
        // Arrange
        var user = MakeUser(isSuperAdmin: true);

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        var jwt = ParseJwt(token);
        jwt.Claims.First(c => c.Type == "isSuperAdmin").Value.Should().Be("true");
    }

    [Fact]
    public void GenerateAccessToken_UserWithMultipleProjects_AllProjectIdsEmbedded()
    {
        // Arrange
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var user = MakeUser();
        user.UserProjectRoles.Add(new UserProjectRole
        {
            Id = Guid.NewGuid(), UserId = user.Id, ProjectId = p1, Role = UserRole.SalesRep
        });
        user.UserProjectRoles.Add(new UserProjectRole
        {
            Id = Guid.NewGuid(), UserId = user.Id, ProjectId = p2, Role = UserRole.SalesManager
        });

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        var jwt = ParseJwt(token);
        var projectIdsClaim = jwt.Claims.First(c => c.Type == "projectIds").Value;
        projectIdsClaim.Should().Contain(p1.ToString());
        projectIdsClaim.Should().Contain(p2.ToString());
    }

    [Fact]
    public void GenerateAccessToken_UserWithNoProjects_ProjectIdsClaimIsEmpty()
    {
        // Arrange
        var user = MakeUser();
        // No UserProjectRoles

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        var jwt = ParseJwt(token);
        var projectIdsClaim = jwt.Claims.First(c => c.Type == "projectIds").Value;
        projectIdsClaim.Should().Be(string.Empty, "empty string when user has no project roles");
    }

    // ── Token expiry ──────────────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_ExpiryIsApproximately15Minutes()
    {
        // Arrange
        var user = MakeUser();
        var before = DateTime.UtcNow;

        // Act
        var token = _tokenService.GenerateAccessToken(user);
        var after = DateTime.UtcNow;

        // Assert
        var jwt = ParseJwt(token);
        jwt.ValidTo.Should().BeAfter(before.AddMinutes(14), "token must expire after ~14 min");
        jwt.ValidTo.Should().BeBefore(after.AddMinutes(16), "token must expire before ~16 min");
    }

    [Fact]
    public void GenerateAccessToken_TwoTokens_HaveDifferentJtiClaims()
    {
        // Arrange
        var user = MakeUser();

        // Act
        var token1 = _tokenService.GenerateAccessToken(user);
        var token2 = _tokenService.GenerateAccessToken(user);

        // Assert — each token must have a unique JWT ID (replay protection)
        var jti1 = ParseJwt(token1).Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jti2 = ParseJwt(token2).Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        jti1.Should().NotBe(jti2, "each access token must have a unique JTI for replay protection");
    }

    // ── Refresh token expiry ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateRefreshToken_ExpiryIsApproximately7Days()
    {
        // Arrange
        var user = MakeUser();
        var before = DateTime.UtcNow;

        // Act
        var (_, entity) = await _tokenService.CreateRefreshTokenAsync(user);

        // Assert
        entity.ExpiresAt.Should().BeAfter(before.AddDays(6), "refresh token must expire after ~6 days");
        entity.ExpiresAt.Should().BeBefore(before.AddDays(8), "refresh token must expire before ~8 days");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static User MakeUser(
        string email = "test@ion.com",
        bool isSuperAdmin = false) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "hash",
        FirstName = "Test",
        LastName = "User",
        IsActive = true,
        IsSuperAdmin = isSuperAdmin,
        UserProjectRoles = new List<UserProjectRole>()
    };

    private static JwtSecurityToken ParseJwt(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ReadJwtToken(token);
    }
}
