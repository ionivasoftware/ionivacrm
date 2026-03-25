using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Tasks.Commands.UpdateTaskStatus;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Tasks;

public class UpdateTaskStatusCommandHandlerTests
{
    private readonly Mock<ICustomerTaskRepository> _taskRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<UpdateTaskStatusCommandHandler>> _loggerMock = new();

    private UpdateTaskStatusCommandHandler CreateHandler() => new(
        _taskRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _taskId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidRequest_ReturnsUpdatedTaskDto()
    {
        // Arrange
        var task = new CustomerTask
        {
            Id = _taskId,
            CustomerId = _customerId,
            ProjectId = _projectId,
            Title = "Complete feature",
            Status = IonCrm.Domain.Enums.TaskStatus.InProgress,
            Priority = TaskPriority.Medium
        };

        _taskRepoMock.Setup(r => r.GetByIdAsync(_taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<CustomerTask>(), It.IsAny<CancellationToken>()))
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        var command = new UpdateTaskStatusCommand(_taskId, IonCrm.Domain.Enums.TaskStatus.Done);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(_taskId);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailure()
    {
        // Arrange
        _taskRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerTask?)null);

        var command = new UpdateTaskStatusCommand(Guid.NewGuid(), IonCrm.Domain.Enums.TaskStatus.Done);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

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
            Title = "Complete feature"
        };

        _taskRepoMock.Setup(r => r.GetByIdAsync(_taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no access

        var command = new UpdateTaskStatusCommand(_taskId, IonCrm.Domain.Enums.TaskStatus.Done);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _taskRepoMock.Verify(r => r.UpdateAsync(It.IsAny<CustomerTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidRequest_StatusIsUpdatedOnEntity()
    {
        // Arrange
        CustomerTask? capturedTask = null;
        var task = new CustomerTask
        {
            Id = _taskId,
            CustomerId = _customerId,
            ProjectId = _projectId,
            Title = "Complete feature",
            Status = IonCrm.Domain.Enums.TaskStatus.Todo
        };

        _taskRepoMock.Setup(r => r.GetByIdAsync(_taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _taskRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<CustomerTask>(), It.IsAny<CancellationToken>()))
            .Callback<CustomerTask, CancellationToken>((t, _) => capturedTask = t)
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        var command = new UpdateTaskStatusCommand(_taskId, IonCrm.Domain.Enums.TaskStatus.Done);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedTask.Should().NotBeNull();
        capturedTask!.Status.Should().Be(IonCrm.Domain.Enums.TaskStatus.Done);
        result.Value!.Status.Should().Be(IonCrm.Domain.Enums.TaskStatus.Done);
    }
}
