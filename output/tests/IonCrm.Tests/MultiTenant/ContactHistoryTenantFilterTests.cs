using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

// Explicit alias to resolve conflict between the Domain entity and the test namespace folder
using ContactHistoryEntity = IonCrm.Domain.Entities.ContactHistory;

namespace IonCrm.Tests.MultiTenant;

/// <summary>
/// Integration tests verifying the DbContext global query filter enforces
/// multi-tenant isolation on ContactHistory records at the database layer.
///
/// CRITICAL: ContactHistory.ProjectId is denormalized from Customer.ProjectId
/// specifically to enable efficient tenant filtering without JOIN.
/// These tests protect against cross-tenant leakage of interaction records.
/// </summary>
public class ContactHistoryTenantFilterTests : IDisposable
{
    private readonly Guid _projectA = Guid.NewGuid();
    private readonly Guid _projectB = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private ApplicationDbContext CreateContext(bool isSuperAdmin, List<Guid>? projectIds = null)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.IsSuperAdmin).Returns(isSuperAdmin);
        mock.Setup(u => u.ProjectIds).Returns(projectIds ?? new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        return new ApplicationDbContext(options, mock.Object);
    }

    private async Task SeedContactHistoriesAsync(ApplicationDbContext ctx)
    {
        // Two customers — one per project
        var custA = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = _projectA, CompanyName = "Alpha Corp"
        };
        var custB = new Customer
        {
            Id = Guid.NewGuid(), ProjectId = _projectB, CompanyName = "Beta Corp"
        };

        // Two contact history records — one per project
        var histA = new ContactHistoryEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = custA.Id,
            ProjectId = _projectA,           // denormalized tenant key
            Type = ContactType.Call,
            Subject = "Call with Alpha",
            ContactedAt = DateTime.UtcNow.AddDays(-1)
        };
        var histB = new ContactHistoryEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = custB.Id,
            ProjectId = _projectB,
            Type = ContactType.Email,
            Subject = "Email with Beta",
            ContactedAt = DateTime.UtcNow.AddDays(-2)
        };

        ctx.Customers.AddRange(custA, custB);
        ctx.ContactHistories.AddRange(histA, histB);
        await ctx.SaveChangesAsync();
    }

    public void Dispose() { }

    // ── Tenant isolation on ContactHistory queries ────────────────────────────

    [Fact]
    public async Task GetAllPagedContactHistories_UserInProjectA_OnlySeesProjectAHistories()
    {
        // Arrange — seed via superadmin, then query as Project A user
        await using var seedCtx = CreateContext(isSuperAdmin: true);
        await SeedContactHistoriesAsync(seedCtx);

        await using var userCtx = CreateContext(isSuperAdmin: false, projectIds: new List<Guid> { _projectA });
        var repo = new ContactHistoryRepository(userCtx);

        // Act
        var (items, total) = await repo.GetPagedAllAsync(null, null, null, null, null, 1, 50);

        // Assert — user in Project A MUST NOT see Project B records
        items.Should().HaveCount(1);
        items[0].ProjectId.Should().Be(_projectA);
        items.Should().NotContain(h => h.ProjectId == _projectB);
        total.Should().Be(1);
    }

    [Fact]
    public async Task GetAllPagedContactHistories_UserInProjectB_OnlySeesProjectBHistories()
    {
        // Arrange
        await using var seedCtx = CreateContext(isSuperAdmin: true);
        await SeedContactHistoriesAsync(seedCtx);

        await using var userCtx = CreateContext(isSuperAdmin: false, projectIds: new List<Guid> { _projectB });
        var repo = new ContactHistoryRepository(userCtx);

        // Act
        var (items, _) = await repo.GetPagedAllAsync(null, null, null, null, null, 1, 50);

        // Assert
        items.Should().HaveCount(1);
        items[0].ProjectId.Should().Be(_projectB);
        items.Should().NotContain(h => h.ProjectId == _projectA);
    }

    [Fact]
    public async Task GetAllPagedContactHistories_SuperAdmin_SeesAllProjects()
    {
        // Arrange
        await using var seedCtx = CreateContext(isSuperAdmin: true);
        await SeedContactHistoriesAsync(seedCtx);

        await using var superCtx = CreateContext(isSuperAdmin: true);
        var repo = new ContactHistoryRepository(superCtx);

        // Act
        var (items, total) = await repo.GetPagedAllAsync(null, null, null, null, null, 1, 50);

        // Assert — SuperAdmin bypasses tenant filter
        total.Should().Be(2);
        items.Should().Contain(h => h.ProjectId == _projectA);
        items.Should().Contain(h => h.ProjectId == _projectB);
    }

    [Fact]
    public async Task GetAllPagedContactHistories_UserWithNoProjects_SeesNothing()
    {
        // Arrange
        await using var seedCtx = CreateContext(isSuperAdmin: true);
        await SeedContactHistoriesAsync(seedCtx);

        await using var noAccessCtx = CreateContext(isSuperAdmin: false, projectIds: new List<Guid>());
        var repo = new ContactHistoryRepository(noAccessCtx);

        // Act
        var (items, total) = await repo.GetPagedAllAsync(null, null, null, null, null, 1, 50);

        // Assert — user with no projects sees zero contact histories
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    // ── Cross-tenant read attempt ─────────────────────────────────────────────

    [Fact]
    public async Task GetByCustomerId_UserInProjectA_CannotSeeProjectBHistory()
    {
        // Arrange — Project B customer and their history seeded via superadmin
        var custBId = Guid.NewGuid();

        await using var seedCtx = CreateContext(isSuperAdmin: true);
        seedCtx.Customers.Add(new Customer
        {
            Id = custBId, ProjectId = _projectB, CompanyName = "Secret Corp"
        });
        seedCtx.ContactHistories.Add(new ContactHistoryEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = custBId,
            ProjectId = _projectB,   // denormalized — the global filter checks THIS
            Type = ContactType.Meeting,
            Subject = "Secret Meeting",
            ContactedAt = DateTime.UtcNow
        });
        await seedCtx.SaveChangesAsync();

        // Project A user queries — global filter on ProjectId prevents visibility
        await using var userCtx = CreateContext(
            isSuperAdmin: false,
            projectIds: new List<Guid> { _projectA });
        var repo = new ContactHistoryRepository(userCtx);

        // Act — user explicitly queries the cross-tenant customer
        var items = await repo.GetByCustomerIdAsync(custBId);

        // Assert — EF's HasQueryFilter hides the record
        items.Should().BeEmpty(
            "Project A user must not see Project B's contact histories via the global tenant filter");
    }

    // ── Soft-delete isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAllPagedContactHistories_SoftDeletedHistory_NotReturned()
    {
        // Arrange — one active, one soft-deleted history in the same project
        await using var seedCtx = CreateContext(isSuperAdmin: true);
        var custA = new Customer { Id = Guid.NewGuid(), ProjectId = _projectA, CompanyName = "Alpha" };
        seedCtx.Customers.Add(custA);
        seedCtx.ContactHistories.AddRange(
            new ContactHistoryEntity
            {
                Id = Guid.NewGuid(), CustomerId = custA.Id, ProjectId = _projectA,
                Type = ContactType.Call, Subject = "Active Call", ContactedAt = DateTime.UtcNow
            },
            new ContactHistoryEntity
            {
                Id = Guid.NewGuid(), CustomerId = custA.Id, ProjectId = _projectA,
                Type = ContactType.Note, Subject = "Deleted Note", ContactedAt = DateTime.UtcNow,
                IsDeleted = true   // soft deleted
            });
        await seedCtx.SaveChangesAsync();

        await using var userCtx = CreateContext(isSuperAdmin: false, projectIds: new List<Guid> { _projectA });
        var repo = new ContactHistoryRepository(userCtx);

        // Act
        var (items, total) = await repo.GetPagedAllAsync(null, null, null, null, null, 1, 50);

        // Assert — soft-deleted history invisible
        total.Should().Be(1);
        items.Should().HaveCount(1);
        items[0].Subject.Should().Be("Active Call");
        items.Should().NotContain(h => h.Subject == "Deleted Note");
    }
}
