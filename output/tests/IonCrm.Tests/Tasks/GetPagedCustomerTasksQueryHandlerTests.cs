using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Tasks.Queries.GetPagedCustomerTasks;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.Tasks;

public class GetPagedCustomerTasksQueryHandlerTests
{
    private readonly Mock<ICustomerTaskRepository> _taskRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetPagedCustomerTasksQueryHandler CreateHandler() => new(
        _taskRepoMock.Object,
        _customerRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidRequest_ReturnsPagedResult()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test" };
        var tasks = new List<CustomerTask>
        {
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Title = "Task 1", Status = IonCrm.Domain.Enums.TaskStatus.Todo },
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Title = "Task 2", Status = IonCrm.Domain.Enums.TaskStatus.InProgress }
        };

        _customerRepoMock.Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _taskRepoMock
            .Setup(r => r.GetPagedByCustomerIdAsync(_customerId, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<CustomerTask>)tasks, 2));

        var query = new GetPagedCustomerTasksQuery { CustomerId = _customerId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
        result.Value.Page.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var query = new GetPagedCustomerTasksQuery { CustomerId = Guid.NewGuid(), Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_AccessDenied_ReturnsFailure()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test" };

        _customerRepoMock.Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no access

        var query = new GetPagedCustomerTasksQuery { CustomerId = _customerId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }
}
