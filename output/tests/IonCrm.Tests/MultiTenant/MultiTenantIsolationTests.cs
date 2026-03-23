using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Application.Customers.Queries.GetCustomers;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.MultiTenant;

public class MultiTenantIsolationTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<CreateCustomerCommandHandler>> _createLoggerMock = new();
    private readonly Mock<ILogger<UpdateCustomerCommandHandler>> _updateLoggerMock = new();
    private readonly Mock<ILogger<DeleteCustomerCommandHandler>> _deleteLoggerMock = new();

    private CreateCustomerCommandHandler CreateCreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _createLoggerMock.Object);

    private UpdateCustomerCommandHandler CreateUpdateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _updateLoggerMock.Object);

    private DeleteCustomerCommandHandler CreateDeleteHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _deleteLoggerMock.Object);

    private GetCustomersQueryHandler CreateGetHandler() => new(_customerRepoMock.Object);

    private Customer CreateCustomer(Guid projectId) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        CompanyName = "Test Customer"
    };

    // ── CreateCustomer tests ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateCustomer_UserNotInProject_ReturnsAccessDenied()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() }); // different project

        var command = new CreateCustomerCommand { ProjectId = projectId, CompanyName = "Corp" };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }

    [Fact]
    public async Task CreateCustomer_UserInProject_Succeeds()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand { ProjectId = projectId, CompanyName = "My Corp" };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task CreateCustomer_SuperAdmin_CanCreateInAnyProject()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        // ProjectIds is empty — superadmin bypasses the check
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand { ProjectId = projectId, CompanyName = "Admin Corp" };

        // Act
        var result = await CreateCreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ── UpdateCustomer tests ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCustomer_UserNotInCustomerProject_ReturnsAccessDenied()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = CreateCustomer(projectId);

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() });

        var command = new UpdateCustomerCommand { Id = customer.Id, CompanyName = "Hacker Corp" };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }

    [Fact]
    public async Task UpdateCustomer_UserInSameProject_Succeeds()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = CreateCustomer(projectId);

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand { Id = customer.Id, CompanyName = "Legit Corp" };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateCustomer_SuperAdmin_CanUpdateAnyProject()
    {
        // Arrange
        var customer = CreateCustomer(Guid.NewGuid());

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand { Id = customer.Id, CompanyName = "Admin Updated" };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ── DeleteCustomer tests ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCustomer_UserNotInCustomerProject_ReturnsAccessDenied()
    {
        // Arrange
        var customer = CreateCustomer(Guid.NewGuid());

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() });

        var command = new DeleteCustomerCommand(customer.Id);

        // Act
        var result = await CreateDeleteHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }

    [Fact]
    public async Task DeleteCustomer_SuperAdmin_CanDeleteAnyProject()
    {
        // Arrange
        var customer = CreateCustomer(Guid.NewGuid());

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _customerRepoMock
            .Setup(r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new DeleteCustomerCommand(customer.Id);

        // Act
        var result = await CreateDeleteHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetCustomers tests ───────────────────────────────────────────────────

    [Fact]
    public async Task GetCustomers_CallsRepositoryWithCorrectFilters()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var search = "Acme";
        var status = CustomerStatus.Active;
        var segment = CustomerSegment.Enterprise;

        _customerRepoMock
            .Setup(r => r.GetPagedAsync(
                search, status, segment, projectId, 2, 15, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Customer>)new List<Customer>(), 0));

        var query = new GetCustomersQuery
        {
            Search = search,
            Status = status,
            Segment = segment,
            AssignedUserId = projectId,
            Page = 2,
            PageSize = 15
        };

        // Act
        var result = await CreateGetHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.GetPagedAsync(
            search, status, segment, projectId, 2, 15, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Cross-tenant isolation test ──────────────────────────────────────────

    [Fact]
    public async Task UpdateCustomer_CrossTenant_ReturnsAccessDenied()
    {
        // Arrange
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        // Customer belongs to projectB
        var customer = CreateCustomer(projectB);

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        // User is in projectA only — not projectB
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectA });

        var command = new UpdateCustomerCommand { Id = customer.Id, CompanyName = "Cross-Tenant Attack" };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _customerRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
