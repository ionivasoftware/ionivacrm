using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.ContactHistory.Queries.GetContactHistoryById;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.ContactHistory;

public class GetContactHistoryByIdQueryHandlerTests
{
    private readonly Mock<IContactHistoryRepository> _historyRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetContactHistoryByIdQueryHandler CreateHandler() => new(
        _historyRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _historyId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidIdAndAccess_ReturnsContactHistoryDto()
    {
        // Arrange
        var history = new Domain.Entities.ContactHistory
        {
            Id = _historyId,
            CustomerId = _customerId,
            ProjectId = _projectId,
            Type = ContactType.Call,
            Subject = "Test call",
            ContactedAt = DateTime.UtcNow.AddHours(-2)
        };

        _historyRepoMock.Setup(r => r.GetByIdAsync(_historyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        // Act
        var result = await CreateHandler().Handle(new GetContactHistoryByIdQuery(_historyId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(_historyId);
        result.Value.CustomerId.Should().Be(_customerId);
        result.Value.Type.Should().Be(ContactType.Call);
        result.Value.Subject.Should().Be("Test call");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailure()
    {
        // Arrange
        _historyRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.ContactHistory?)null);

        // Act
        var result = await CreateHandler().Handle(new GetContactHistoryByIdQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_AccessDenied_ReturnsFailure()
    {
        // Arrange
        var history = new Domain.Entities.ContactHistory
        {
            Id = _historyId,
            CustomerId = _customerId,
            ProjectId = _projectId,
            Type = ContactType.Email,
            ContactedAt = DateTime.UtcNow.AddHours(-1)
        };

        _historyRepoMock.Setup(r => r.GetByIdAsync(_historyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no access

        // Act
        var result = await CreateHandler().Handle(new GetContactHistoryByIdQuery(_historyId), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }
}
