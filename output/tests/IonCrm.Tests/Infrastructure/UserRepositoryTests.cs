using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace IonCrm.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="UserRepository"/> using an in-memory EF Core database.
/// </summary>
public class UserRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly UserRepository _repository;

    public UserRepositoryTests()
    {
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(x => x.ProjectIds).Returns(new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context    = new ApplicationDbContext(options, currentUserMock.Object);
        _repository = new UserRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // ── GetByEmailAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByEmailAsync_ExistingEmail_ReturnsUser()
    {
        // Arrange
        var user = CreateUser("alice@example.com");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByEmailAsync("alice@example.com");

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task GetByEmailAsync_NonExistentEmail_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByEmailAsync("nobody@example.com");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByEmailAsync_EmailNormalisedBeforeLookup_FindsUser()
    {
        // Arrange
        var user = CreateUser("alice@example.com");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act — input with spaces and uppercase
        var result = await _repository.GetByEmailAsync("  ALICE@EXAMPLE.COM  ");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByEmailAsync_SoftDeletedUser_ReturnsNull()
    {
        // Arrange
        var user = CreateUser("deleted@example.com");
        user.IsDeleted = true;
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByEmailAsync("deleted@example.com");

        // Assert — soft-delete filter applied
        result.Should().BeNull();
    }

    // ── GetByIdWithRolesAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdWithRolesAsync_ValidId_ReturnsUserWithRoles()
    {
        // Arrange
        var project = CreateProject();
        var user    = CreateUser("bob@example.com");
        _context.Projects.Add(project);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var role = new UserProjectRole
        {
            UserId    = user.Id,
            ProjectId = project.Id,
            Role      = UserRole.SalesRep
        };
        _context.UserProjectRoles.Add(role);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdWithRolesAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result!.UserProjectRoles.Should().HaveCount(1);
        result.UserProjectRoles.First().Role.Should().Be(UserRole.SalesRep);
    }

    [Fact]
    public async Task GetByIdWithRolesAsync_EmptyGuid_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByIdWithRolesAsync(Guid.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdWithRolesAsync_UnknownId_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByIdWithRolesAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    // ── EmailExistsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task EmailExistsAsync_ExistingEmail_ReturnsTrue()
    {
        // Arrange
        _context.Users.Add(CreateUser("charlie@example.com"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.EmailExistsAsync("charlie@example.com");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task EmailExistsAsync_NonExistentEmail_ReturnsFalse()
    {
        // Act
        var result = await _repository.EmailExistsAsync("nobody@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task EmailExistsAsync_NormalisesInputBeforeCheck()
    {
        // Arrange
        _context.Users.Add(CreateUser("dave@example.com"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.EmailExistsAsync("  DAVE@EXAMPLE.COM  ");

        // Assert
        result.Should().BeTrue();
    }

    // ── GetByProjectIdAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetByProjectIdAsync_UsersInProject_ReturnsThem()
    {
        // Arrange
        var project = CreateProject();
        var user    = CreateUser("eve@example.com");
        _context.Projects.Add(project);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.UserProjectRoles.Add(new UserProjectRole
        {
            UserId    = user.Id,
            ProjectId = project.Id,
            Role      = UserRole.ProjectAdmin
        });
        await _context.SaveChangesAsync();

        // Act
        var results = await _repository.GetByProjectIdAsync(project.Id);

        // Assert
        results.Should().ContainSingle(u => u.Email == "eve@example.com");
    }

    [Fact]
    public async Task GetByProjectIdAsync_NoUsers_ReturnsEmpty()
    {
        // Act
        var results = await _repository.GetByProjectIdAsync(Guid.NewGuid());

        // Assert
        results.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static User CreateUser(string email) => new()
    {
        Id           = Guid.NewGuid(),
        Email        = email,
        PasswordHash = "$2a$12$dummyhash",
        FirstName    = "Test",
        LastName     = "User",
        IsActive     = true,
        IsSuperAdmin = false
    };

    private static Project CreateProject() => new()
    {
        Id       = Guid.NewGuid(),
        Name     = "Test Project",
        IsActive = true
    };
}
