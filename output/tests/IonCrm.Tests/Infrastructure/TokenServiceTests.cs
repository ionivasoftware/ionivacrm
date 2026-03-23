using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace IonCrm.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="TokenService"/> using in-memory EF Core.
/// </summary>
public class TokenServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IConfiguration _configuration;

    public TokenServiceTests()
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(x => x.ProjectIds).Returns(new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, currentUserMock.Object);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]                      = "test-super-secret-key-minimum-32-chars!",
                ["Jwt:Issuer"]                   = "TestIssuer",
                ["Jwt:Audience"]                 = "TestAudience",
                ["Jwt:AccessTokenExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenExpiryDays"]   = "7"
            })
            .Build();

        var loggerMock = new Mock<ILogger<TokenService>>();
        _tokenService  = new TokenService(_configuration, _context, loggerMock.Object);
    }

    public void Dispose() => _context.Dispose();

    // ── GenerateAccessToken ───────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_ValidUser_ReturnsNonEmptyJwt()
    {
        // Arrange
        var user = CreateUser();

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        // JWT format: three base64url parts separated by dots
        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void GenerateAccessToken_SuperAdminUser_HasCorrectClaim()
    {
        // Arrange
        var user = CreateUser(isSuperAdmin: true);

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert — decode payload to verify claim presence
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt     = handler.ReadJwtToken(token);

        jwt.Claims
           .First(c => c.Type == "isSuperAdmin")
           .Value.Should().Be("true");
    }

    [Fact]
    public void GenerateAccessToken_UserWithRoles_EmbedsProjectIds()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var user      = CreateUser();
        user.UserProjectRoles.Add(new UserProjectRole
        {
            Id        = Guid.NewGuid(),
            UserId    = user.Id,
            ProjectId = projectId,
            Role      = Domain.Enums.UserRole.SalesRep
        });

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt     = handler.ReadJwtToken(token);

        jwt.Claims
           .First(c => c.Type == "projectIds")
           .Value.Should().Contain(projectId.ToString());
    }

    [Fact]
    public void GenerateAccessToken_ContainsEmailClaim()
    {
        // Arrange
        var user = CreateUser(email: "test@ion.com");

        // Act
        var token = _tokenService.GenerateAccessToken(user);

        // Assert
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt     = handler.ReadJwtToken(token);

        jwt.Claims
           .First(c => c.Type == "email")
           .Value.Should().Be("test@ion.com");
    }

    // ── CreateRefreshTokenAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CreateRefreshTokenAsync_ValidUser_ReturnsRawTokenAndEntity()
    {
        // Arrange
        var user = CreateUser();

        // Act
        var (rawToken, entity) = await _tokenService.CreateRefreshTokenAsync(user);

        // Assert
        rawToken.Should().NotBeNullOrWhiteSpace();
        entity.Should().NotBeNull();
        entity.UserId.Should().Be(user.Id);
        entity.IsRevoked.Should().BeFalse();
        entity.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateRefreshTokenAsync_StoresHashedToken_NotRaw()
    {
        // Arrange
        var user = CreateUser();

        // Act
        var (rawToken, entity) = await _tokenService.CreateRefreshTokenAsync(user);

        // Assert — stored token must NOT equal the raw token
        entity.Token.Should().NotBe(rawToken);
        // SHA-256 hex = 64 lowercase hex chars
        entity.Token.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task CreateRefreshTokenAsync_TwoCalls_ProduceDifferentTokens()
    {
        // Arrange
        var user = CreateUser();

        // Act
        var (raw1, _) = await _tokenService.CreateRefreshTokenAsync(user);
        var (raw2, _) = await _tokenService.CreateRefreshTokenAsync(user);

        // Assert
        raw1.Should().NotBe(raw2);
    }

    // ── GetActiveRefreshTokenAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetActiveRefreshTokenAsync_ValidRawToken_ReturnsEntity()
    {
        // Arrange
        var user = CreateUser();
        var (rawToken, _) = await _tokenService.CreateRefreshTokenAsync(user);

        // Act
        var result = await _tokenService.GetActiveRefreshTokenAsync(rawToken);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetActiveRefreshTokenAsync_WrongToken_ReturnsNull()
    {
        // Act
        var result = await _tokenService.GetActiveRefreshTokenAsync("completely-wrong-token");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveRefreshTokenAsync_RevokedToken_ReturnsNull()
    {
        // Arrange
        var user = CreateUser();
        var (rawToken, entity) = await _tokenService.CreateRefreshTokenAsync(user);
        entity.IsRevoked = true;
        await _context.SaveChangesAsync();

        // Act
        var result = await _tokenService.GetActiveRefreshTokenAsync(rawToken);

        // Assert
        result.Should().BeNull();
    }

    // ── RevokeRefreshTokenAsync ───────────────────────────────────────────────

    [Fact]
    public async Task RevokeRefreshTokenAsync_ValidToken_SetsIsRevokedTrue()
    {
        // Arrange
        var user = CreateUser();
        var (rawToken, entity) = await _tokenService.CreateRefreshTokenAsync(user);

        // Act
        await _tokenService.RevokeRefreshTokenAsync(rawToken);

        // Assert
        var updated = await _context.RefreshTokens.FindAsync(entity.Id);
        updated!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_UnknownToken_DoesNotThrow()
    {
        // Act
        var act = async () => await _tokenService.RevokeRefreshTokenAsync("unknown-token");

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ── RevokeAllUserRefreshTokensAsync ───────────────────────────────────────

    [Fact]
    public async Task RevokeAllUserRefreshTokensAsync_MultipleTokens_RevokesAll()
    {
        // Arrange
        var user = CreateUser();
        await _tokenService.CreateRefreshTokenAsync(user);
        await _tokenService.CreateRefreshTokenAsync(user);
        await _tokenService.CreateRefreshTokenAsync(user);

        // Act
        await _tokenService.RevokeAllUserRefreshTokensAsync(user.Id);

        // Assert
        var remaining = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
            .CountAsync();
        remaining.Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static User CreateUser(
        string email       = "user@ion.com",
        bool isSuperAdmin  = false) => new()
    {
        Id           = Guid.NewGuid(),
        Email        = email,
        PasswordHash = "$2a$12$dummyhash",
        FirstName    = "Test",
        LastName     = "User",
        IsActive     = true,
        IsSuperAdmin = isSuperAdmin,
        UserProjectRoles = new List<UserProjectRole>()
    };
}
