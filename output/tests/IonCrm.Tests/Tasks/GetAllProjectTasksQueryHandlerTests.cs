using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Tasks.Queries.GetAllProjectTasks;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using TaskStatus = IonCrm.Domain.Enums.TaskStatus;

namespace IonCrm.Tests.Tasks;

/// <summary>
/// Tests for <see cref="GetAllProjectTasksQueryHandler"/> — project-wide paged task list.
/// </summary>
public class GetAllProjectTasksQueryHandlerTests
{
    private readonly Mock<ICustomerTaskRepository> _taskRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetAllProjectTasksQueryHandler CreateHandler() => new(
        _taskRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();

    private static IReadOnlyList<CustomerTask> MakeTasks(Guid projectId, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new CustomerTask
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                CustomerId = Guid.NewGuid(),
                Title = $"Task {i}",
                Status = TaskStatus.Todo,
                Priority = TaskPriority.Medium
            })
            .ToList()
            .AsReadOnly();

    [Fact]
    public async Task Handle_AuthorizedUser_ReturnsPagedResult()
    {
        // Arrange
        var tasks = MakeTasks(_projectId, 3);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _taskRepoMock
            .Setup(r => r.GetPagedByProjectAsync(
                _projectId, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<CustomerTask>)tasks, 3));

        var query = new GetAllProjectTasksQuery { ProjectId = _projectId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no projects

        var query = new GetAllProjectTasksQuery { ProjectId = _projectId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _taskRepoMock.Verify(
            r => r.GetPagedByProjectAsync(
                It.IsAny<Guid>(), It.IsAny<TaskStatus?>(), It.IsAny<TaskPriority?>(),
                It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SuperAdmin_BypassesTenantCheck()
    {
        // Arrange
        var tasks = MakeTasks(_projectId, 2);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no explicit projects

        _taskRepoMock
            .Setup(r => r.GetPagedByProjectAsync(
                _projectId, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<CustomerTask>)tasks, 2));

        var query = new GetAllProjectTasksQuery { ProjectId = _projectId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithStatusFilter_PassesFilterToRepository()
    {
        // Arrange
        var tasks = MakeTasks(_projectId, 1);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _taskRepoMock
            .Setup(r => r.GetPagedByProjectAsync(
                _projectId, TaskStatus.InProgress, null, null, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<CustomerTask>)tasks, 1));

        var query = new GetAllProjectTasksQuery
        {
            ProjectId = _projectId,
            Status = TaskStatus.InProgress,
            Page = 1,
            PageSize = 10
        };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        _taskRepoMock.Verify(
            r => r.GetPagedByProjectAsync(
                _projectId, TaskStatus.InProgress, null, null, 1, 10, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithPriorityAndAssignedUserFilter_PassesAllFiltersToRepository()
    {
        // Arrange
        var assignedUserId = Guid.NewGuid();
        var tasks = MakeTasks(_projectId, 2);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _taskRepoMock
            .Setup(r => r.GetPagedByProjectAsync(
                _projectId, null, TaskPriority.High, assignedUserId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<CustomerTask>)tasks, 2));

        var query = new GetAllProjectTasksQuery
        {
            ProjectId = _projectId,
            Priority = TaskPriority.High,
            AssignedUserId = assignedUserId,
            Page = 1,
            PageSize = 20
        };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_EmptyProject_ReturnsEmptyPagedResult()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _taskRepoMock
            .Setup(r => r.GetPagedByProjectAsync(
                _projectId, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<CustomerTask>)new List<CustomerTask>(), 0));

        var query = new GetAllProjectTasksQuery { ProjectId = _projectId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }
}
