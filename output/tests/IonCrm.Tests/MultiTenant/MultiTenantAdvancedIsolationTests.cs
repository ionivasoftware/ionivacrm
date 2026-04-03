using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Application.Customers.Queries.GetCustomerById;
using IonCrm.Application.Features.Sync.Queries.GetSyncLogs;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using IonCrm.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.MultiTenant;

/// <summary>
/// Advanced multi-tenant isolation tests covering:
/// - User in multiple projects sees data from all assigned projects
/// - GetCustomerById cross-tenant returns 404 (security-through-obscurity)
/// - SuperAdmin GetCustomerById bypasses tenant filter
/// - SyncLogs tenant isolation: users can only see their own project logs
/// - SyncLogs: SuperAdmin sees all projects
/// - SyncLogs: user with no projects → access denied
/// - SyncLogs: user requesting foreign project → access denied
/// </summary>
public class MultiTenantAdvancedIsolationTests : IDisposable
{
    private readonly Guid _projectA = Guid.NewGuid();
    private readonly Guid _projectB = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private ApplicationDbContext CreateDbContext(bool isSuperAdmin, List<Guid>? projectIds = null)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(u => u.IsSuperAdmin).Returns(isSuperAdmin);
        mock.Setup(u => u.ProjectIds).Returns(projectIds ?? new List<Guid>());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        return new ApplicationDbContext(options, mock.Object);
    }

    private async Task SeedCustomersAsync()
    {
        await using var ctx = CreateDbContext(isSuperAdmin: true);
        ctx.Customers.AddRange(
            new Customer { Id = Guid.NewGuid(), ProjectId = _projectA, CompanyName = "Alpha Corp" },
            new Customer { Id = Guid.NewGuid(), ProjectId = _projectA, CompanyName = "Alpha Sub" },
            new Customer { Id = Guid.NewGuid(), ProjectId = _projectB, CompanyName = "Beta Corp" });
        await ctx.SaveChangesAsync();
    }

    public void Dispose() { }

    // ── Multi-project user sees all their projects ────────────────────────────

    [Fact]
    public async Task GetCustomers_UserInBothProjects_SeesDataFromAllTheirProjects()
    {
        // Arrange — user belongs to Project A AND Project B
        await SeedCustomersAsync();

        await using var ctx = CreateDbContext(
            isSuperAdmin: false,
            projectIds: new List<Guid> { _projectA, _projectB });
        var repo = new CustomerRepository(ctx);

        // Act
        var (items, total) = await repo.GetPagedAsync(null, null, null, null, null, null, 1, 50);

        // Assert — user sees ALL three customers across both projects
        total.Should().Be(3, "user in both projects must see all their data");
        items.Should().Contain(c => c.ProjectId == _projectA);
        items.Should().Contain(c => c.ProjectId == _projectB);
    }

    [Fact]
    public async Task GetCustomers_UserInProjectAOnly_DoesNotSeeProjectBData()
    {
        // Arrange
        await SeedCustomersAsync();

        await using var ctx = CreateDbContext(
            isSuperAdmin: false,
            projectIds: new List<Guid> { _projectA });
        var repo = new CustomerRepository(ctx);

        // Act
        var (items, total) = await repo.GetPagedAsync(null, null, null, null, null, null, 1, 50);

        // Assert — strict isolation: Project A user sees ONLY Project A rows
        total.Should().Be(2, "only Project A has 2 customers");
        items.Should().NotContain(c => c.CompanyName == "Beta Corp",
            "Project B data must be invisible to Project A user");
    }

    // ── GetCustomerById cross-tenant ─────────────────────────────────────────

    [Fact]
    public async Task GetCustomerById_UserInSameProject_ReturnsCustomer()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer
            {
                Id = customerId,
                ProjectId = _projectA,
                CompanyName = "Alpha Corp"
            });
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA });

        var handler = new GetCustomerByIdQueryHandler(customerRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetCustomerByIdQuery(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(customerId);
        result.Value.CompanyName.Should().Be("Alpha Corp");
    }

    [Fact]
    public async Task GetCustomerById_UserInDifferentProject_ReturnsNotFound()
    {
        // Arrange — customer belongs to Project B, user is in Project A only
        var customerId = Guid.NewGuid();
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer
            {
                Id = customerId,
                ProjectId = _projectB,      // customer belongs to B
                CompanyName = "Beta Secret"
            });
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA }); // user is in A only

        var handler = new GetCustomerByIdQueryHandler(customerRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetCustomerByIdQuery(customerId), CancellationToken.None);

        // Assert — security-through-obscurity: returns "not found", not "forbidden"
        result.IsFailure.Should().BeTrue("cross-tenant access must be denied");
        result.FirstError.Should().Contain("not found",
            "must NOT reveal existence of data — return 'not found' not 'access denied'");
    }

    [Fact]
    public async Task GetCustomerById_SuperAdmin_CanSeeAnyProjectsCustomer()
    {
        // Arrange — customer belongs to Project B, SuperAdmin has no explicit project memberships
        var customerId = Guid.NewGuid();
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer
            {
                Id = customerId,
                ProjectId = _projectB,
                CompanyName = "Any Corp"
            });
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no explicit memberships

        var handler = new GetCustomerByIdQueryHandler(customerRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetCustomerByIdQuery(customerId), CancellationToken.None);

        // Assert — SuperAdmin bypasses tenant filter
        result.IsSuccess.Should().BeTrue("SuperAdmin must be able to retrieve any customer");
        result.Value!.CompanyName.Should().Be("Any Corp");
    }

    [Fact]
    public async Task GetCustomerById_CustomerNotFound_ReturnsNotFound()
    {
        // Arrange
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA });

        var handler = new GetCustomerByIdQueryHandler(customerRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetCustomerByIdQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    // ── SyncLogs tenant isolation ─────────────────────────────────────────────

    [Fact]
    public async Task GetSyncLogs_UserWithNoProjects_ReturnsAccessDenied()
    {
        // Arrange — user has zero project memberships
        var syncRepoMock = new Mock<ISyncLogRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no projects

        var handler = new GetSyncLogsQueryHandler(syncRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetSyncLogsQuery(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue("user with no project membership cannot view any sync logs");
        result.FirstError.Should().Contain("Access denied");
        syncRepoMock.Verify(
            r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "repository must not be called when access is denied upfront");
    }

    [Fact]
    public async Task GetSyncLogs_UserRequestingForeignProjectId_ReturnsAccessDenied()
    {
        // Arrange — user is in Project A but requests Project B's logs
        var syncRepoMock = new Mock<ISyncLogRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA }); // user is in A

        var handler = new GetSyncLogsQueryHandler(syncRepoMock.Object, currentUserMock.Object);

        // Act — request Project B's logs
        var result = await handler.Handle(new GetSyncLogsQuery(ProjectId: _projectB), CancellationToken.None);

        // Assert — cross-tenant access must be blocked
        result.IsFailure.Should().BeTrue("user must not be able to read another tenant's sync logs");
        result.FirstError.Should().Contain("Access denied");
        syncRepoMock.Verify(
            r => r.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSyncLogs_UserRequestingOwnProjectId_Succeeds()
    {
        // Arrange — user is in Project A and requests Project A's logs (valid)
        var syncRepoMock = new Mock<ISyncLogRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA });

        syncRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), _projectA,
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<SyncLog>(), 0));

        var handler = new GetSyncLogsQueryHandler(syncRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetSyncLogsQuery(ProjectId: _projectA), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("user may read their own project's sync logs");
        syncRepoMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), _projectA,
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSyncLogs_SuperAdmin_CanQueryAnyProject()
    {
        // Arrange — SuperAdmin has no explicit project memberships but can query any project
        var syncRepoMock = new Mock<ISyncLogRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        syncRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), _projectB,
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<SyncLog>
            {
                new SyncLog { Id = Guid.NewGuid(), ProjectId = _projectB, Source = SyncSource.SaasA,
                    Direction = SyncDirection.Inbound, EntityType = "customer", Status = SyncStatus.Success }
            }, 1));

        var handler = new GetSyncLogsQueryHandler(syncRepoMock.Object, currentUserMock.Object);

        // Act — SuperAdmin queries Project B
        var result = await handler.Handle(new GetSyncLogsQuery(ProjectId: _projectB), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("SuperAdmin bypasses all tenant restrictions");
        result.Value!.Items.Should().HaveCount(1);
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSyncLogs_SuperAdmin_NoProjectIdFilter_QueriesAll()
    {
        // Arrange — SuperAdmin without specifying a projectId → should see everything
        var syncRepoMock = new Mock<ISyncLogRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        syncRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), null,        // null → all projects
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<SyncLog>(), 0));

        var handler = new GetSyncLogsQueryHandler(syncRepoMock.Object, currentUserMock.Object);

        // Act
        var result = await handler.Handle(new GetSyncLogsQuery(), CancellationToken.None); // no ProjectId

        // Assert
        result.IsSuccess.Should().BeTrue("SuperAdmin can query without restricting to a specific project");
        syncRepoMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), null,
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSyncLogs_UserWithNoProjectIdInQuery_DefaultsToFirstUserProject()
    {
        // Arrange — user in Project A sends query without ProjectId → auto-scoped to Project A
        var syncRepoMock = new Mock<ISyncLogRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA });

        Guid? capturedProjectId = null;
        syncRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<SyncSource?>(), It.IsAny<SyncDirection?>(), It.IsAny<SyncStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, Guid?, SyncSource?, SyncDirection?, SyncStatus?, DateTime?, DateTime?, CancellationToken>(
                (_, _, pid, _, _, _, _, _, _) => capturedProjectId = pid)
            .ReturnsAsync((new List<SyncLog>(), 0));

        var handler = new GetSyncLogsQueryHandler(syncRepoMock.Object, currentUserMock.Object);

        // Act — no ProjectId in query
        var result = await handler.Handle(new GetSyncLogsQuery(), CancellationToken.None);

        // Assert — must default to user's first project
        result.IsSuccess.Should().BeTrue();
        capturedProjectId.Should().Be(_projectA,
            "when no ProjectId supplied the handler must default to the user's first project");
    }

    // ── Soft-delete isolation with InMemory DB ────────────────────────────────

    [Fact]
    public async Task SoftDeletedCustomer_NotVisibleToAnyRegularUser()
    {
        // Arrange — seed an active and a soft-deleted customer in Project A
        await using var seedCtx = CreateDbContext(isSuperAdmin: true);
        var activeId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        seedCtx.Customers.AddRange(
            new Customer { Id = activeId,  ProjectId = _projectA, CompanyName = "Active Corp" },
            new Customer { Id = deletedId, ProjectId = _projectA, CompanyName = "Deleted Corp", IsDeleted = true });
        await seedCtx.SaveChangesAsync();

        await using var userCtx = CreateDbContext(
            isSuperAdmin: false,
            projectIds: new List<Guid> { _projectA });
        var repo = new CustomerRepository(userCtx);

        // Act — regular user queries Project A
        var (items, total) = await repo.GetPagedAsync(null, null, null, null, null, null, 1, 50);

        // Assert — soft-deleted record must be invisible
        total.Should().Be(1);
        items.Should().ContainSingle(c => c.Id == activeId);
        items.Should().NotContain(c => c.Id == deletedId, "soft-deleted records must be filtered out");
    }

    [Fact]
    public async Task CreateCustomer_ProjectIdMismatch_UserCannotInjectForeignTenantData()
    {
        // Arrange — user is in Project A but tries to create a customer in Project B
        var loggerMock = new Mock<ILogger<CreateCustomerCommandHandler>>();
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA }); // user is in A

        var handler = new CreateCustomerCommandHandler(
            customerRepoMock.Object,
            currentUserMock.Object,
            loggerMock.Object);

        var command = new CreateCustomerCommand
        {
            ProjectId = _projectB,         // attacker supplies Project B ID
            CompanyName = "Injected Corp"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — access denied because user is not a member of Project B
        result.IsFailure.Should().BeTrue("cross-tenant data injection must be blocked");
        result.FirstError.Should().Contain("Access denied");
        customerRepoMock.Verify(
            r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "repository Add must NOT be called when access is denied");
    }

    [Fact]
    public async Task DeleteCustomer_UserInSameProject_CallsDeleteExactlyOnce()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var loggerMock = new Mock<ILogger<DeleteCustomerCommandHandler>>();

        var customer = new Customer { Id = customerId, ProjectId = _projectA, CompanyName = "To Delete" };

        customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA });
        customerRepoMock
            .Setup(r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new DeleteCustomerCommandHandler(
            customerRepoMock.Object,
            currentUserMock.Object,
            loggerMock.Object);

        // Act
        var result = await handler.Handle(new DeleteCustomerCommand(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        customerRepoMock.Verify(
            r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()),
            Times.Once,
            "DeleteAsync must be called exactly once on success");
    }

    [Fact]
    public async Task UpdateCustomer_CrossTenant_DeleteNeverCalled()
    {
        // Arrange — attacker tries to update a customer in Project B while only in Project A
        var customerId = Guid.NewGuid();
        var customerRepoMock = new Mock<ICustomerRepository>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var loggerMock = new Mock<ILogger<UpdateCustomerCommandHandler>>();

        customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = customerId, ProjectId = _projectB, CompanyName = "Target" });
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectA });

        var handler = new UpdateCustomerCommandHandler(
            customerRepoMock.Object,
            currentUserMock.Object,
            loggerMock.Object);

        var command = new UpdateCustomerCommand { Id = customerId, CompanyName = "Hacked" };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "UpdateAsync must NEVER be called when access is denied");
    }
}
