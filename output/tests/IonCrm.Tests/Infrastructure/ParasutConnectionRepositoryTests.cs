using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="ParasutConnectionRepository"/> using an in-memory EF Core database.
/// Covers: global connection fallback, project-specific lookup, soft-delete filtering, GetAllAsync.
/// </summary>
public class ParasutConnectionRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ParasutConnectionRepository _repository;

    public ParasutConnectionRepositoryTests()
    {
        // Use SuperAdmin to bypass global query filters
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(x => x.ProjectIds).Returns(new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, currentUserMock.Object);
        _repository = new ParasutConnectionRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // ── GetByProjectIdAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetByProjectIdAsync_ExistingProjectConnection_ReturnsIt()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var conn = CreateProjectConnection(projectId);
        _context.ParasutConnections.Add(conn);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByProjectIdAsync(projectId);

        // Assert
        result.Should().NotBeNull();
        result!.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task GetByProjectIdAsync_NoProjectConnection_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByProjectIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByProjectIdAsync_SoftDeletedConnection_ReturnsNull()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var conn = CreateProjectConnection(projectId);
        conn.IsDeleted = true;
        _context.ParasutConnections.Add(conn);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByProjectIdAsync(projectId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByProjectIdAsync_DoesNotReturnGlobalConnection()
    {
        // Arrange — only a global connection exists, NOT a project-specific one
        var projectId = Guid.NewGuid();
        var globalConn = CreateGlobalConnection();
        _context.ParasutConnections.Add(globalConn);
        await _context.SaveChangesAsync();

        // Act — strict project lookup (no fallback)
        var result = await _repository.GetByProjectIdAsync(projectId);

        // Assert
        result.Should().BeNull();
    }

    // ── GetGlobalAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetGlobalAsync_GlobalConnectionExists_ReturnsIt()
    {
        // Arrange
        var globalConn = CreateGlobalConnection();
        _context.ParasutConnections.Add(globalConn);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetGlobalAsync();

        // Assert
        result.Should().NotBeNull();
        result!.ProjectId.Should().BeNull();
    }

    [Fact]
    public async Task GetGlobalAsync_NoGlobalConnection_ReturnsNull()
    {
        // Act
        var result = await _repository.GetGlobalAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGlobalAsync_SoftDeletedGlobalConnection_ReturnsNull()
    {
        // Arrange
        var globalConn = CreateGlobalConnection();
        globalConn.IsDeleted = true;
        _context.ParasutConnections.Add(globalConn);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetGlobalAsync();

        // Assert
        result.Should().BeNull();
    }

    // ── GetEffectiveConnectionAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetEffectiveConnectionAsync_ProjectSpecificExists_ReturnsProjectConnection()
    {
        // Arrange — both project-specific and global connections exist
        var projectId = Guid.NewGuid();
        var projectConn = CreateProjectConnection(projectId, companyId: 111);
        var globalConn = CreateGlobalConnection(companyId: 999);
        _context.ParasutConnections.AddRange(projectConn, globalConn);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEffectiveConnectionAsync(projectId);

        // Assert — project-specific wins
        result.Should().NotBeNull();
        result!.CompanyId.Should().Be(111);
    }

    [Fact]
    public async Task GetEffectiveConnectionAsync_NoProjectSpecific_FallsBackToGlobal()
    {
        // Arrange — only global connection exists
        var projectId = Guid.NewGuid();
        var globalConn = CreateGlobalConnection(companyId: 999);
        _context.ParasutConnections.Add(globalConn);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetEffectiveConnectionAsync(projectId);

        // Assert — falls back to global
        result.Should().NotBeNull();
        result!.CompanyId.Should().Be(999);
        result.ProjectId.Should().BeNull();
    }

    [Fact]
    public async Task GetEffectiveConnectionAsync_NeitherProjectNorGlobal_ReturnsNull()
    {
        // Act
        var result = await _repository.GetEffectiveConnectionAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllNonDeletedConnections()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var active1 = CreateProjectConnection(projectId);
        var active2 = CreateGlobalConnection();
        var deleted = CreateProjectConnection(Guid.NewGuid());
        deleted.IsDeleted = true;

        _context.ParasutConnections.AddRange(active1, active2, deleted);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(c => c.IsDeleted);
    }

    // ── AddAsync / UpdateAsync / DeleteAsync ──────────────────────────────────

    [Fact]
    public async Task AddAsync_AssignsNewIdAndPersists()
    {
        // Arrange
        var conn = new ParasutConnection
        {
            ProjectId = Guid.NewGuid(),
            CompanyId = 42,
            ClientId = "cid",
            ClientSecret = "csec",
            Username = "user@example.com",
            Password = "pass"
        };

        // Act
        var result = await _repository.AddAsync(conn);

        // Assert
        result.Id.Should().NotBe(Guid.Empty);
        var persisted = await _context.ParasutConnections.FindAsync(result.Id);
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_SetsSoftDeleteFlag()
    {
        // Arrange
        var conn = CreateProjectConnection(Guid.NewGuid());
        _context.ParasutConnections.Add(conn);
        await _context.SaveChangesAsync();

        // Act
        await _repository.DeleteAsync(conn);

        // Assert — soft-deleted
        var inDb = await _context.ParasutConnections
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == conn.Id);
        inDb.IsDeleted.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ParasutConnection CreateProjectConnection(Guid projectId, long companyId = 100) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        CompanyId = companyId,
        ClientId = "cid",
        ClientSecret = "csec",
        Username = "user@test.com",
        Password = "pass"
    };

    private static ParasutConnection CreateGlobalConnection(long companyId = 200) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = null,   // null = global
        CompanyId = companyId,
        ClientId = "global-cid",
        ClientSecret = "global-csec",
        Username = "global@test.com",
        Password = "global-pass"
    };
}
