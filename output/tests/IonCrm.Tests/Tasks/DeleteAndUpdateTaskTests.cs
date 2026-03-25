using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Tasks.Commands.DeleteCustomerTask;
using IonCrm.Application.Tasks.Commands.UpdateCustomerTask;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using TaskStatus = IonCrm.Domain.Enums.TaskStatus;

namespace IonCrm.Tests.Tasks;

/// <summary>
/// Tests for <see cref="DeleteCustomerTaskCommandHandler"/> and
/// <see cref="UpdateCustomerTaskCommandHandler"/>.
/// </summary>
public class DeleteAndUpdateTaskTests
{
    private readonly Mock<ICustomerTaskRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();
    private readonly Mock<ILogger<DeleteCustomerTaskCommandHandler>> _deleteLoggerMock = new();
    private readonly Mock<ILogger<UpdateCustomerTaskCommandHandler>> _updateLoggerMock = new();

    private DeleteCustomerTaskCommandHandler DeleteHandler() => new(
        _repoMock.Object, _userMock.Object, _deleteLoggerMock.Object);

    private UpdateCustomerTaskCommandHandler UpdateHandler() => new(
        _repoMock.Object, _userMock.Object, _updateLoggerMock.Object);

    private CustomerTask MakeTask(Guid? projectId = null) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId ?? Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Title = "Original Title",
        Status = TaskStatus.Todo,
        Priority = TaskPriority.Medium
    };

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_TaskNotFound_ReturnsFailure()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerTask?)null);

        // Act
        var result = await DeleteHandler().Handle(
            new DeleteCustomerTaskCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Delete_AccessDenied_ReturnsFailure()
    {
        // Arrange
        var task = MakeTask();
        _repoMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds)
            .Returns(new List<Guid> { Guid.NewGuid() }); // different project

        // Act
        var result = await DeleteHandler().Handle(
            new DeleteCustomerTaskCommand(task.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _repoMock.Verify(
            r => r.DeleteAsync(It.IsAny<CustomerTask>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Delete_AuthorizedUser_CallsDeleteAsync()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var task = MakeTask(projectId);
        _repoMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _repoMock
            .Setup(r => r.DeleteAsync(task, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await DeleteHandler().Handle(
            new DeleteCustomerTaskCommand(task.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repoMock.Verify(
            r => r.DeleteAsync(task, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Delete_SuperAdmin_CanDeleteAnyTask()
    {
        // Arrange
        var task = MakeTask();
        _repoMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _repoMock
            .Setup(r => r.DeleteAsync(task, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await DeleteHandler().Handle(
            new DeleteCustomerTaskCommand(task.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_TaskNotFound_ReturnsFailure()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerTask?)null);

        // Act
        var result = await UpdateHandler().Handle(
            new UpdateCustomerTaskCommand { Id = Guid.NewGuid(), Title = "Updated" },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Update_AccessDenied_ReturnsFailure()
    {
        // Arrange
        var task = MakeTask();
        _repoMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds)
            .Returns(new List<Guid> { Guid.NewGuid() }); // different project

        // Act
        var result = await UpdateHandler().Handle(
            new UpdateCustomerTaskCommand { Id = task.Id, Title = "Hijacked" },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _repoMock.Verify(
            r => r.UpdateAsync(It.IsAny<CustomerTask>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_AuthorizedUser_AllFieldsUpdated()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var task = MakeTask(projectId);
        var assignedUserId = Guid.NewGuid();
        var dueDate = DateTime.UtcNow.AddDays(3);

        _repoMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _repoMock
            .Setup(r => r.UpdateAsync(task, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerTaskCommand
        {
            Id = task.Id,
            Title = "Updated Title",
            Description = "Updated Desc",
            DueDate = dueDate,
            Priority = TaskPriority.High,
            Status = TaskStatus.InProgress,
            AssignedUserId = assignedUserId
        };

        // Act
        var result = await UpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        task.Title.Should().Be("Updated Title");
        task.Description.Should().Be("Updated Desc");
        task.DueDate.Should().Be(dueDate);
        task.Priority.Should().Be(TaskPriority.High);
        task.Status.Should().Be(TaskStatus.InProgress);
        task.AssignedUserId.Should().Be(assignedUserId);
        result.Value!.Title.Should().Be("Updated Title");
        result.Value.Status.Should().Be(TaskStatus.InProgress);
    }

    [Fact]
    public async Task Update_SuperAdmin_CanUpdateAnyTask()
    {
        // Arrange
        var task = MakeTask();
        _repoMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _repoMock
            .Setup(r => r.UpdateAsync(task, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await UpdateHandler().Handle(
            new UpdateCustomerTaskCommand { Id = task.Id, Title = "Admin Update" },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        task.Title.Should().Be("Admin Update");
    }
}
