using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.ContactHistory.Queries.GetContactHistories;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.ContactHistory;

/// <summary>
/// Tests for <see cref="GetContactHistoriesQueryHandler"/> — flat (non-paged) contact history list per customer.
/// </summary>
public class GetContactHistoriesQueryHandlerTests
{
    private readonly Mock<IContactHistoryRepository> _historyRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetContactHistoriesQueryHandler CreateHandler() => new(
        _historyRepoMock.Object,
        _customerRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    private static IonCrm.Domain.Entities.ContactHistory MakeHistory(Guid customerId, Guid projectId) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ProjectId = projectId,
            Type = ContactType.Call,
            Subject = "Test call",
            ContactedAt = DateTime.UtcNow.AddHours(-1)
        };

    [Fact]
    public async Task Handle_ValidRequest_ReturnsAllContactHistories()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test Co" };
        var histories = new List<IonCrm.Domain.Entities.ContactHistory>
        {
            MakeHistory(_customerId, _projectId),
            MakeHistory(_customerId, _projectId),
            MakeHistory(_customerId, _projectId)
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _historyRepoMock
            .Setup(r => r.GetByCustomerIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IonCrm.Domain.Entities.ContactHistory>)histories);

        var query = new GetContactHistoriesQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value!.All(h => h.CustomerId == _customerId).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var query = new GetContactHistoriesQuery(Guid.NewGuid());

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
        _historyRepoMock.Verify(
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
            .Returns(new List<Guid> { Guid.NewGuid() }); // different project — access denied

        var query = new GetContactHistoriesQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _historyRepoMock.Verify(
            r => r.GetByCustomerIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanAccessAnyCustomersHistory()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test Co" };
        var histories = new List<IonCrm.Domain.Entities.ContactHistory>
        {
            MakeHistory(_customerId, _projectId)
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no explicit projects
        _historyRepoMock
            .Setup(r => r.GetByCustomerIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IonCrm.Domain.Entities.ContactHistory>)histories);

        var query = new GetContactHistoriesQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_CustomerWithNoHistory_ReturnsEmptyList()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Quiet Co" };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _historyRepoMock
            .Setup(r => r.GetByCustomerIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IonCrm.Domain.Entities.ContactHistory>)new List<IonCrm.Domain.Entities.ContactHistory>());

        var query = new GetContactHistoriesQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ContactType.Call)]
    [InlineData(ContactType.Email)]
    [InlineData(ContactType.Meeting)]
    [InlineData(ContactType.Note)]
    [InlineData(ContactType.WhatsApp)]
    [InlineData(ContactType.Visit)]
    public async Task Handle_VariousContactTypes_AllReturnedInDto(ContactType contactType)
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Multi-type Co" };
        var history = new IonCrm.Domain.Entities.ContactHistory
        {
            Id = Guid.NewGuid(),
            CustomerId = _customerId,
            ProjectId = _projectId,
            Type = contactType,
            ContactedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _historyRepoMock
            .Setup(r => r.GetByCustomerIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IonCrm.Domain.Entities.ContactHistory>)new List<IonCrm.Domain.Entities.ContactHistory> { history });

        var query = new GetContactHistoriesQuery(_customerId);

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value!.First().Type.Should().Be(contactType);
    }
}
