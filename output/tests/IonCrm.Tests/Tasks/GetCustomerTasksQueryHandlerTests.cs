using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Tasks.Queries.GetCustomerTasks;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using TaskStatus = IonCrm.Domain.Enums.TaskStatus;

namespace IonCrm.Tests.Tasks;

/// <summary>
/// Tests for <see cref="GetCustomerTasksQueryHandler"/> — flat (non-paged) list of tasks per customer.
/// </summary>
public class GetCustomerTasksQueryHandlerTests
{
    private readonly Mock<ICustomerTaskRepository> _taskRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetCustomerTasksQueryHandler CreateHandler() => new(
        _taskRepoMock.Object,
        _customerRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidRequest_ReturnsAllCustomerTasks()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test Co" };
        var tasks = new List<CustomerTask>
        {
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Title = "Task A", Status = TaskStatus.Todo },
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Title = "Task B", Status = TaskStatus.InProgress },
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Title = "Task C", Status = TaskStatus.Done }
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _taskRepoMock
            .Setup(r => r.GetByCustomerIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CustomerTask>)tasks);

        var query = new GetCustomerTasksQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value!.Select(t => t.Title).Should().Contain("Task A", "Task B", "Task C");
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var query = new GetCustomerTasksQuery(Guid.NewGuid());

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
        _taskRepoMock.Verify(
            r => r.GetByCustomerIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UserNotInCustomersProject_ReturnsFailure()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test Co" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds)
            .Returns(new List<Guid> { Guid.NewGuid() }); // different project

        var query = new GetCustomerTasksQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _taskRepoMock.Verify(
            r => r.GetByCustomerIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanAccessAnyCustomersTasks()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test Co" };
        var tasks = new List<CustomerTask>
        {
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Title = "Admin Task" }
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no explicit access
        _taskRepoMock
            .Setup(r => r.GetByCustomerIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CustomerTask>)tasks);

        var query = new GetCustomerTasksQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_CustomerHasNoTasks_ReturnsEmptyList()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Empty Co" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _taskRepoMock
            .Setup(r => r.GetByCustomerIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CustomerTask>)new List<CustomerTask>());

        var query = new GetCustomerTasksQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
