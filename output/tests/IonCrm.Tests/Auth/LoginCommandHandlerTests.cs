using IonCrm.Application.Auth.Commands.Login;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Auth;

public class LoginCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ITokenService> _tokenServiceMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<ILogger<LoginCommandHandler>> _loggerMock = new();

    private LoginCommandHandler CreateHandler() => new(
        _userRepoMock.Object,
        _tokenServiceMock.Object,
        _passwordHasherMock.Object,
        _loggerMock.Object);

    private User CreateActiveUser(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Email = "user@example.com",
        PasswordHash = "hashed_password",
        FirstName = "Jane",
        LastName = "Doe",
        IsActive = true,
        IsSuperAdmin = false
    };

    private void SetupValidUserLookup(User user)
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
    }

    private void SetupTokenServices(string accessToken = "access_token", string refreshToken = "refresh_token")
    {
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(It.IsAny<User>()))
            .Returns(accessToken);
        _tokenServiceMock
            .Setup(t => t.GetAccessTokenExpiresAt())
            .Returns(DateTime.UtcNow.AddHours(1));
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((refreshToken, new RefreshToken { Token = refreshToken, ExpiresAt = DateTime.UtcNow.AddDays(7) }));
    }

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsSuccessWithTokens()
    {
        // Arrange
        var user = CreateActiveUser();
        SetupValidUserLookup(user);
        SetupTokenServices("my_access_token", "my_refresh_token");

        var command = new LoginCommand(user.Email, "password123");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("my_access_token");
        result.Value.RefreshToken.Should().Be("my_refresh_token");
        result.Value.User.Should().NotBeNull();
        result.Value.User.Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsFailure()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var command = new LoginCommand("nonexistent@example.com", "password123");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Handle_WrongPassword_ReturnsFailure()
    {
        // Arrange
        var user = CreateActiveUser();
        _userRepoMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(p => p.Verify(It.IsAny<string>(), user.PasswordHash))
            .Returns(false);

        var command = new LoginCommand(user.Email, "wrong_password");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Handle_InactiveUser_ReturnsFailure()
    {
        // Arrange
        var user = CreateActiveUser();
        user.IsActive = false;

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(p => p.Verify(It.IsAny<string>(), user.PasswordHash))
            .Returns(true);

        var command = new LoginCommand(user.Email, "password123");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("deactivated");
    }

    [Fact]
    public async Task Handle_EmailIsNormalizedBeforeLookup()
    {
        // Arrange
        var user = CreateActiveUser();
        user.Email = "user@example.com";

        string capturedEmail = string.Empty;
        _userRepoMock
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((email, _) => capturedEmail = email)
            .ReturnsAsync((User?)null);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Pass email with extra spaces and mixed case
        var command = new LoginCommand("  USER@EXAMPLE.COM  ", "password123");

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        capturedEmail.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Handle_ValidLogin_AccessTokenExpiresInFuture()
    {
        // Arrange
        var user = CreateActiveUser();
        SetupValidUserLookup(user);
        SetupTokenServices();

        var command = new LoginCommand(user.Email, "password123");
        var before = DateTime.UtcNow;

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessTokenExpiresAt.Should().BeAfter(before);
        result.Value.AccessTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Handle_ValidLogin_CreatesRefreshToken()
    {
        // Arrange
        var user = CreateActiveUser();
        SetupValidUserLookup(user);
        SetupTokenServices("access_token", "new_refresh_token");

        var command = new LoginCommand(user.Email, "password123");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.RefreshToken.Should().Be("new_refresh_token");
        _tokenServiceMock.Verify(
            t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
