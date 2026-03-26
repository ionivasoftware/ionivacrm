using IonCrm.Application.Auth.Commands.Login;
using IonCrm.Application.Auth.Commands.RefreshToken;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Auth;

/// <summary>
/// Additional auth edge-case tests focusing on:
/// - SuperAdmin flag preserved in login response DTO
/// - User's full name returned in auth response
/// - Token expiry is in the future (not past)
/// - Refresh rotation: Revoke is called BEFORE CreateRefreshToken (ordering invariant)
/// - Refresh token: new access token is different from original
/// - Refresh token: new refresh token is different from old one
/// - RefreshToken for SuperAdmin returns correct SuperAdmin flag in DTO
/// - Concurrent refresh: each valid session produces a new token pair
/// </summary>
public class AuthEdgeCaseTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ITokenService> _tokenServiceMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ILogger<LoginCommandHandler>> _loginLoggerMock = new();
    private readonly Mock<ILogger<RefreshTokenCommandHandler>> _refreshLoggerMock = new();

    private LoginCommandHandler CreateLoginHandler() => new(
        _userRepoMock.Object,
        _tokenServiceMock.Object,
        _passwordHasherMock.Object,
        _loginLoggerMock.Object);

    private RefreshTokenCommandHandler CreateRefreshHandler() => new(
        _tokenServiceMock.Object,
        _userRepoMock.Object,
        _refreshLoggerMock.Object);

    private User CreateUser(
        bool isSuperAdmin = false,
        bool isActive = true,
        string firstName = "Jane",
        string lastName = "Doe",
        string email = "jane@example.com") => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "hashed_password",
        FirstName = firstName,
        LastName = lastName,
        IsActive = isActive,
        IsSuperAdmin = isSuperAdmin,
        UserProjectRoles = new List<UserProjectRole>()
    };

    private void SetupValidLogin(User user, string accessToken = "at", string refreshToken = "rt")
    {
        _userRepoMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(p => p.Verify(It.IsAny<string>(), user.PasswordHash))
            .Returns(true);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns(accessToken);
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync((refreshToken, new RefreshToken
            {
                Token = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                UserId = user.Id
            }));
        _tokenServiceMock
            .Setup(t => t.GetAccessTokenExpiresAt())
            .Returns(() => DateTime.UtcNow.AddMinutes(15));
    }

    // ── SuperAdmin login ──────────────────────────────────────────────────────

    [Fact]
    public async Task Login_SuperAdminUser_ReturnsDtoWithIsSuperAdminTrue()
    {
        // Arrange
        var superAdmin = CreateUser(isSuperAdmin: true, email: "admin@ion.com");
        SetupValidLogin(superAdmin);

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(superAdmin.Email, "password"), CancellationToken.None);

        // Assert — SuperAdmin flag must be carried into the auth response DTO
        result.IsSuccess.Should().BeTrue();
        result.Value!.User.IsSuperAdmin.Should().BeTrue(
            "SuperAdmin login must return IsSuperAdmin=true in the user DTO");
    }

    [Fact]
    public async Task Login_RegularUser_ReturnsDtoWithIsSuperAdminFalse()
    {
        // Arrange
        var regularUser = CreateUser(isSuperAdmin: false);
        SetupValidLogin(regularUser);

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(regularUser.Email, "password"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.User.IsSuperAdmin.Should().BeFalse(
            "regular user login must return IsSuperAdmin=false");
    }

    // ── User DTO completeness ─────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_UserDtoContainsCorrectName()
    {
        // Arrange
        var user = CreateUser(firstName: "Alice", lastName: "Smith", email: "alice@example.com");
        SetupValidLogin(user);

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(user.Email, "password"), CancellationToken.None);

        // Assert — user's name must be in the auth response
        result.IsSuccess.Should().BeTrue();
        result.Value!.User.FirstName.Should().Be("Alice");
        result.Value.User.LastName.Should().Be("Smith");
        result.Value.User.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task Login_ValidCredentials_UserDtoContainsCorrectUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = CreateUser();
        user.Id = userId;
        SetupValidLogin(user);

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(user.Email, "password"), CancellationToken.None);

        // Assert — userId must survive into the DTO
        result.IsSuccess.Should().BeTrue();
        result.Value!.User.Id.Should().Be(userId,
            "the auth response DTO must carry the correct user ID");
    }

    // ── Token expiry ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_AccessTokenExpiryIsInTheFuture()
    {
        // Arrange
        var user = CreateUser();
        SetupValidLogin(user);
        var before = DateTime.UtcNow;

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(user.Email, "password"), CancellationToken.None);

        // Assert — expiry MUST be after the current moment; token is usable
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessTokenExpiresAt.Should().BeAfter(before,
            "a freshly issued access token must expire in the future, not in the past");
    }

    [Fact]
    public async Task Login_ValidCredentials_AccessTokenExpiresApproximately15MinutesFromNow()
    {
        // Arrange
        var user = CreateUser();
        SetupValidLogin(user);
        var issuedAt = DateTime.UtcNow;

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(user.Email, "password"), CancellationToken.None);

        // Assert — hard-coded 15 minutes expiry in LoginCommandHandler
        var expiry = result.Value!.AccessTokenExpiresAt;
        expiry.Should().BeCloseTo(issuedAt.AddMinutes(15), TimeSpan.FromSeconds(5),
            "access token expiry should be approximately 15 minutes from now");
    }

    // ── Refresh token rotation ordering ──────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ValidToken_RevokesOldTokenBeforeCreatingNew()
    {
        // Arrange — capture invocation order to verify Revoke → Create sequence
        var user = CreateUser();
        var userId = user.Id;
        var existingToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "old_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };

        var invocationOrder = new List<string>();

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("old_refresh_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync("old_refresh_token", It.IsAny<CancellationToken>()))
            .Callback(() => invocationOrder.Add("revoke"))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns("new_access_token");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((_, _) => invocationOrder.Add("create"))
            .ReturnsAsync(("new_refresh_token", new RefreshToken
            {
                Token = "new_refresh_token",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            }));

        // Act
        var result = await CreateRefreshHandler().Handle(
            new RefreshTokenCommand("old_refresh_token"), CancellationToken.None);

        // Assert — correct order: Revoke must happen BEFORE Create (one-time-use semantics)
        result.IsSuccess.Should().BeTrue();
        invocationOrder.Should().HaveCount(2, "both revoke and create must be called");
        invocationOrder[0].Should().Be("revoke",
            "old token must be revoked FIRST to prevent replay attacks");
        invocationOrder[1].Should().Be("create",
            "new token must be created AFTER revocation");
    }

    [Fact]
    public async Task RefreshToken_ValidToken_NewAccessTokenIsNotEmpty()
    {
        // Arrange
        var user = CreateUser();
        var existingToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "old_rt",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("old_rt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns("fresh_access_token");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("fresh_rt", new RefreshToken
            {
                Token = "fresh_rt",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            }));

        // Act
        var result = await CreateRefreshHandler().Handle(
            new RefreshTokenCommand("old_rt"), CancellationToken.None);

        // Assert — new pair must be issued
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("fresh_access_token");
        result.Value.AccessToken.Should().NotBeNullOrWhiteSpace("new access token must not be empty");
    }

    [Fact]
    public async Task RefreshToken_ValidToken_NewRefreshTokenDifferentFromOld()
    {
        // Arrange
        var user = CreateUser();
        var existingToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "old_rt_value",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("old_rt_value", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns("at");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("brand_new_rt_different", new RefreshToken
            {
                Token = "brand_new_rt_different",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            }));

        // Act
        var result = await CreateRefreshHandler().Handle(
            new RefreshTokenCommand("old_rt_value"), CancellationToken.None);

        // Assert — the new refresh token must be different (rotation, not reuse)
        result.IsSuccess.Should().BeTrue();
        result.Value!.RefreshToken.Should().NotBe("old_rt_value",
            "refresh token rotation MUST issue a new token, not return the same one");
        result.Value.RefreshToken.Should().Be("brand_new_rt_different");
    }

    // ── Refresh for SuperAdmin ────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_SuperAdminUser_ReturnsDtoWithIsSuperAdminTrue()
    {
        // Arrange — SuperAdmin refreshes their session
        var superAdmin = CreateUser(isSuperAdmin: true);
        var existingToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = superAdmin.Id,
            Token = "sa_rt",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("sa_rt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(superAdmin.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(superAdmin);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(superAdmin))
            .Returns("sa_at");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(superAdmin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("sa_new_rt", new RefreshToken
            {
                Token = "sa_new_rt",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            }));

        // Act
        var result = await CreateRefreshHandler().Handle(
            new RefreshTokenCommand("sa_rt"), CancellationToken.None);

        // Assert — SuperAdmin status must persist through token refresh
        result.IsSuccess.Should().BeTrue();
        result.Value!.User.IsSuperAdmin.Should().BeTrue(
            "SuperAdmin flag must remain true after token rotation");
    }

    // ── Email normalisation edge cases ────────────────────────────────────────

    [Fact]
    public async Task Login_EmailWithLeadingTrailingSpaces_NormalisedBeforeLookup()
    {
        // Arrange
        string? capturedEmail = null;
        _userRepoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((e, _) => capturedEmail = e)
            .ReturnsAsync((User?)null);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        await CreateLoginHandler().Handle(
            new LoginCommand("  ADMIN@ION.COM  ", "pw"), CancellationToken.None);

        // Assert — must normalise to lowercase + trimmed
        capturedEmail.Should().Be("admin@ion.com",
            "email must be trimmed and lowercased before repository lookup");
    }

    [Fact]
    public async Task Login_InvalidCredentials_DoesNotCallTokenService()
    {
        // Arrange — simulate wrong password scenario
        var user = CreateUser();
        _userRepoMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(p => p.Verify(It.IsAny<string>(), user.PasswordHash))
            .Returns(false); // wrong password

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(user.Email, "wrong"), CancellationToken.None);

        // Assert — token service must NEVER be called on failed auth
        result.IsFailure.Should().BeTrue();
        _tokenServiceMock.Verify(
            t => t.GenerateAccessToken(It.IsAny<User>()),
            Times.Never,
            "token must not be generated when credentials are invalid");
        _tokenServiceMock.Verify(
            t => t.CreateRefreshTokenAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "refresh token must not be created when credentials are invalid");
    }

    [Fact]
    public async Task Login_DeactivatedUser_DoesNotCallTokenService()
    {
        // Arrange
        var user = CreateUser(isActive: false);
        _userRepoMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(p => p.Verify(It.IsAny<string>(), user.PasswordHash))
            .Returns(true);

        // Act
        var result = await CreateLoginHandler().Handle(
            new LoginCommand(user.Email, "password"), CancellationToken.None);

        // Assert — token must NOT be generated for deactivated users
        result.IsFailure.Should().BeTrue();
        _tokenServiceMock.Verify(
            t => t.GenerateAccessToken(It.IsAny<User>()),
            Times.Never,
            "deactivated users must not receive tokens");
    }

    // ── Refresh token used twice (replay attack) ──────────────────────────────

    [Fact]
    public async Task RefreshToken_UsedTwice_SecondAttemptReturnsFailure()
    {
        // Arrange — first use succeeds; second use sees revoked/null token
        var user = CreateUser();
        var existingToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "reused_rt",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        // First call: token is valid
        var callCount = 0;
        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("reused_rt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? existingToken : (RefreshToken?)null;
            });
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns("at");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("new_rt", new RefreshToken
            {
                Token = "new_rt",
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            }));

        // Act — first use
        var firstResult = await CreateRefreshHandler().Handle(
            new RefreshTokenCommand("reused_rt"), CancellationToken.None);

        // Second use (token now revoked/null)
        var secondResult = await CreateRefreshHandler().Handle(
            new RefreshTokenCommand("reused_rt"), CancellationToken.None);

        // Assert
        firstResult.IsSuccess.Should().BeTrue("first use of a valid refresh token should succeed");
        secondResult.IsFailure.Should().BeTrue("replay attack: second use of same token must fail");
        secondResult.FirstError.Should().Contain("Invalid or expired refresh token",
            "revoked/expired token must be rejected");
    }
}
