using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Tasks.Queries.GetCustomerTaskById;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.Tasks;

public class GetCustomerTaskByIdQueryHandlerTests
{
    private readonly Mock<ICustomerTaskRepository> _taskRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetCustomerTaskByIdQueryHandler CreateHandler() => new(
        _taskRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _taskId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidIdAndAccess_ReturnsCustomerTaskDto()
    {
        // Arrange
        var task = new CustomerTask
        {
            Id = _taskId,
            CustomerId = _customerId,
            ProjectId = _projectId,
            Title = "Fix bug",
            Status = IonCrm.Domain.Enums.TaskStatus.InProgress,
            Priority = TaskPriority.High
        };

        _taskRepoMock.Setup(r => r.GetByIdAsync(_taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        // Act
        var result = await CreateHandler().Handle(new GetCustomerTaskByIdQuery(_taskId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(_taskId);
        result.Value.Title.Should().Be("Fix bug");
        result.Value.Status.Should().Be(IonCrm.Domain.Enums.TaskStatus.InProgress);
        result.Value.Priority.Should().Be(TaskPriority.High);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailure()
    {
        // Arrange
        _taskRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerTask?)null);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerTaskByIdQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_AccessDenied_ReturnsFailure()
    {
        // Arrange
        var task = new CustomerTask
        {
            Id = _taskId,
            CustomerId = _customerId,
            ProjectId = _projectId,
            Title = "Fix bug"
        };

        _taskRepoMock.Setup(r => r.GetByIdAsync(_taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no access

        // Act
        var result = await CreateHandler().Handle(new GetCustomerTaskByIdQuery(_taskId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }
}
