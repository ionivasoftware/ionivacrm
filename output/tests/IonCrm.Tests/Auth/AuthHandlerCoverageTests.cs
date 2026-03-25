using IonCrm.Application.Auth.Commands.Logout;
using IonCrm.Application.Auth.Commands.RegisterUser;
using IonCrm.Application.Auth.Queries.GetCurrentUser;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Auth;

/// <summary>
/// Tests for Logout, RegisterUser, and GetCurrentUser handlers.
/// These are AUTH EDGE CASE tests for role-based access and token management.
/// </summary>
public class AuthHandlerCoverageTests
{
    // ── LogoutCommandHandler ──────────────────────────────────────────────────

    private readonly Mock<ITokenService> _tokenServiceMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<LogoutCommandHandler>> _logoutLoggerMock = new();
    private readonly Mock<ILogger<RegisterUserCommandHandler>> _registerLoggerMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ILogger<GetCurrentUserQueryHandler>> _getCurrentUserLoggerMock = new();

    private LogoutCommandHandler CreateLogoutHandler() => new(
        _tokenServiceMock.Object, _logoutLoggerMock.Object);

    private RegisterUserCommandHandler CreateRegisterHandler() => new(
        _userRepoMock.Object, _passwordHasherMock.Object, _registerLoggerMock.Object);

    private GetCurrentUserQueryHandler CreateGetCurrentUserHandler() => new(
        _userRepoMock.Object, _currentUserMock.Object, _getCurrentUserLoggerMock.Object);

    // ── Logout ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_SingleSession_RevokesSpecificToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var refreshToken = "token_to_revoke";
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync(refreshToken, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new LogoutCommand(refreshToken, userId, LogoutEverywhere: false);

        // Act
        var result = await CreateLogoutHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _tokenServiceMock.Verify(
            t => t.RevokeRefreshTokenAsync(refreshToken, It.IsAny<CancellationToken>()),
            Times.Once);
        _tokenServiceMock.Verify(
            t => t.RevokeAllUserRefreshTokensAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Logout_AllSessions_RevokesAllUserTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _tokenServiceMock
            .Setup(t => t.RevokeAllUserRefreshTokensAsync(userId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new LogoutCommand("any_token", userId, LogoutEverywhere: true);

        // Act
        var result = await CreateLogoutHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _tokenServiceMock.Verify(
            t => t.RevokeAllUserRefreshTokensAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
        _tokenServiceMock.Verify(
            t => t.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── RegisterUser ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterUser_NewEmail_CreatesUserAndReturnsDto()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.EmailExistsAsync("jane@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.Hash("SecurePass123!"))
            .Returns("hashed_password");
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);

        var command = new RegisterUserCommand(
            "jane@example.com", "SecurePass123!", "Jane", "Doe");

        // Act
        var result = await CreateRegisterHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("jane@example.com");
        result.Value.FirstName.Should().Be("Jane");
        result.Value.LastName.Should().Be("Doe");
        result.Value.IsSuperAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterUser_DuplicateEmail_ReturnsFailure()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.EmailExistsAsync("dup@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new RegisterUserCommand(
            "dup@example.com", "password", "Jane", "Doe");

        // Act
        var result = await CreateRegisterHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("already registered");
        _userRepoMock.Verify(
            r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterUser_EmailNormalisedToLowercase()
    {
        // Arrange
        string capturedEmail = string.Empty;
        _userRepoMock
            .Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((email, _) => capturedEmail = email)
            .ReturnsAsync(false);
        _passwordHasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hash");
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);

        var command = new RegisterUserCommand(
            "  UPPER@EXAMPLE.COM  ", "pass", "A", "B");

        // Act
        await CreateRegisterHandler().Handle(command, CancellationToken.None);

        // Assert — email trimmed and lowercased before duplicate check
        capturedEmail.Should().Be("upper@example.com");
    }

    [Fact]
    public async Task RegisterUser_SuperAdminFlag_IsPersistedCorrectly()
    {
        // Arrange
        User? addedUser = null;
        _userRepoMock
            .Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hash");
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => addedUser = u)
            .ReturnsAsync((User u, CancellationToken _) => u);

        var command = new RegisterUserCommand(
            "admin@ion.com", "pass", "Admin", "User", IsSuperAdmin: true);

        // Act
        await CreateRegisterHandler().Handle(command, CancellationToken.None);

        // Assert
        addedUser.Should().NotBeNull();
        addedUser!.IsSuperAdmin.Should().BeTrue();
        addedUser.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterUser_PasswordIsHashed_NotStoredInPlainText()
    {
        // Arrange
        const string plainPassword = "MyPlainPassword!";
        const string hashedPassword = "bcrypt_hashed_value";
        User? addedUser = null;

        _userRepoMock
            .Setup(r => r.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.Hash(plainPassword))
            .Returns(hashedPassword);
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => addedUser = u)
            .ReturnsAsync((User u, CancellationToken _) => u);

        var command = new RegisterUserCommand("user@test.com", plainPassword, "T", "U");

        // Act
        await CreateRegisterHandler().Handle(command, CancellationToken.None);

        // Assert
        addedUser!.PasswordHash.Should().Be(hashedPassword);
        addedUser.PasswordHash.Should().NotBe(plainPassword);
    }

    // ── GetCurrentUser ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentUser_Authenticated_ReturnsUserDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId, Email = "me@example.com",
            FirstName = "Me", LastName = "Test",
            IsActive = true, IsSuperAdmin = false,
            PasswordHash = "hash",
            UserProjectRoles = new List<UserProjectRole>()
        };

        _currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(u => u.UserId).Returns(userId);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var query = new GetCurrentUserQuery();

        // Act
        var result = await CreateGetCurrentUserHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("me@example.com");
    }

    [Fact]
    public async Task GetCurrentUser_NotAuthenticated_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var query = new GetCurrentUserQuery();

        // Act
        var result = await CreateGetCurrentUserHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Not authenticated");
    }

    [Fact]
    public async Task GetCurrentUser_UserNotInDatabase_ReturnsFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(u => u.UserId).Returns(userId);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var query = new GetCurrentUserQuery();

        // Act
        var result = await CreateGetCurrentUserHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }
}
