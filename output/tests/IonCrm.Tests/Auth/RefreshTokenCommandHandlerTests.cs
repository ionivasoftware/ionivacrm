using IonCrm.Application.Auth.Commands.RefreshToken;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Auth;

public class RefreshTokenCommandHandlerTests
{
    private readonly Mock<ITokenService> _tokenServiceMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ILogger<RefreshTokenCommandHandler>> _loggerMock = new();

    private RefreshTokenCommandHandler CreateHandler() => new(
        _tokenServiceMock.Object,
        _userRepoMock.Object,
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

    private RefreshToken CreateRefreshToken(Guid userId, string rawToken = "valid_refresh_token") => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Token = rawToken,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        IsRevoked = false
    };

    [Fact]
    public async Task Handle_ValidToken_ReturnsNewTokenPair()
    {
        // Arrange
        var user = CreateActiveUser();
        var existingToken = CreateRefreshToken(user.Id, "old_refresh_token");

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("old_refresh_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync("old_refresh_token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns("new_access_token");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("new_refresh_token", new RefreshToken { Token = "new_refresh_token", ExpiresAt = DateTime.UtcNow.AddDays(7) }));

        var command = new RefreshTokenCommand("old_refresh_token");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("new_access_token");
        result.Value.RefreshToken.Should().Be("new_refresh_token");
        result.Value.User.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_InvalidToken_ReturnsFailure()
    {
        // Arrange
        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        var command = new RefreshTokenCommand("invalid_token");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be("Invalid or expired refresh token.");
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsFailure()
    {
        // Arrange
        // Service returns null for expired tokens (same as invalid)
        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        var command = new RefreshTokenCommand("expired_token");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be("Invalid or expired refresh token.");
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingToken = CreateRefreshToken(userId);

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var command = new RefreshTokenCommand("valid_token");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found or deactivated");
    }

    [Fact]
    public async Task Handle_InactiveUser_ReturnsFailure()
    {
        // Arrange
        var user = CreateActiveUser();
        user.IsActive = false;
        var existingToken = CreateRefreshToken(user.Id);

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var command = new RefreshTokenCommand("valid_token");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found or deactivated");
    }

    [Fact]
    public async Task Handle_ValidRefresh_RevokesOldToken()
    {
        // Arrange
        var user = CreateActiveUser();
        var existingToken = CreateRefreshToken(user.Id, "old_token");

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("old_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync("old_token", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns("access_token");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("new_token", new RefreshToken { Token = "new_token", ExpiresAt = DateTime.UtcNow.AddDays(7) }));

        var command = new RefreshTokenCommand("old_token");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _tokenServiceMock.Verify(
            t => t.RevokeRefreshTokenAsync("old_token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRefresh_IssuesNewRefreshToken()
    {
        // Arrange
        var user = CreateActiveUser();
        var existingToken = CreateRefreshToken(user.Id, "old_token");

        _tokenServiceMock
            .Setup(t => t.GetActiveRefreshTokenAsync("old_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingToken);
        _userRepoMock
            .Setup(r => r.GetByIdWithRolesAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenServiceMock
            .Setup(t => t.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user))
            .Returns("access_token");
        _tokenServiceMock
            .Setup(t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("brand_new_refresh", new RefreshToken { Token = "brand_new_refresh", ExpiresAt = DateTime.UtcNow.AddDays(7) }));

        var command = new RefreshTokenCommand("old_token");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.RefreshToken.Should().Be("brand_new_refresh");
        _tokenServiceMock.Verify(
            t => t.CreateRefreshTokenAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
