using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.ContactHistory.Queries.GetAllContactHistories;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.ContactHistory;

/// <summary>Unit tests for GetAllContactHistoriesQueryHandler.</summary>
public class GetAllContactHistoriesQueryHandlerTests
{
    private readonly Mock<IContactHistoryRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetAllContactHistoriesQueryHandler CreateHandler() => new(
        _repoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();

    private static IReadOnlyList<Domain.Entities.ContactHistory> BuildHistories(int count, Guid projectId)
        => Enumerable.Range(1, count).Select(i => new Domain.Entities.ContactHistory
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProjectId = projectId,
            Type = ContactType.Call,
            ContactedAt = DateTime.UtcNow.AddDays(-i)
        }).ToList().AsReadOnly();

    [Fact]
    public async Task Handle_ReturnsPagedResult_ForAccessibleProject()
    {
        // Arrange
        var histories = BuildHistories(3, _projectId);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _repoMock.Setup(r => r.GetPagedAllAsync(
                _projectId,
                It.IsAny<Guid?>(),
                It.IsAny<ContactType?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Domain.Entities.ContactHistory>)histories, 3));

        var query = new GetAllContactHistoriesQuery { ProjectId = _projectId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ProjectIdNotInUserProjects_ReturnsAccessDenied()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // user has no projects

        var query = new GetAllContactHistoriesQuery { ProjectId = _projectId };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _repoMock.Verify(r => r.GetPagedAllAsync(
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<ContactType?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanAccessAnyProject()
    {
        // Arrange
        var histories = BuildHistories(2, _projectId);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        _repoMock.Setup(r => r.GetPagedAllAsync(
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<ContactType?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Domain.Entities.ContactHistory>)histories, 2));

        var query = new GetAllContactHistoriesQuery { ProjectId = _projectId };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoProjectFilter_UsesGlobalQueryFilter()
    {
        // Arrange — no ProjectId in query; global filter handles tenant scoping
        var histories = BuildHistories(5, _projectId);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _repoMock.Setup(r => r.GetPagedAllAsync(
                null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Domain.Entities.ContactHistory>)histories, 5));

        var query = new GetAllContactHistoriesQuery { Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task Handle_PageSizeClamped_ToMaximum100()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _repoMock.Setup(r => r.GetPagedAllAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<ContactType?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Domain.Entities.ContactHistory>)new List<Domain.Entities.ContactHistory>(), 0));

        var query = new GetAllContactHistoriesQuery { Page = 1, PageSize = 9999 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repoMock.Verify(r => r.GetPagedAllAsync(
            null, null, null, null, null, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }
}
