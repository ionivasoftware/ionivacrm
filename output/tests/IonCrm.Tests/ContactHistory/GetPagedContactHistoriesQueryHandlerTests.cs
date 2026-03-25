using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.ContactHistory.Queries.GetPagedContactHistories;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.ContactHistory;

public class GetPagedContactHistoriesQueryHandlerTests
{
    private readonly Mock<IContactHistoryRepository> _historyRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetPagedContactHistoriesQueryHandler CreateHandler() => new(
        _historyRepoMock.Object,
        _customerRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidRequest_ReturnsPagedResult()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test" };
        var histories = new List<Domain.Entities.ContactHistory>
        {
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Type = ContactType.Call, ContactedAt = DateTime.UtcNow.AddHours(-1) },
            new() { Id = Guid.NewGuid(), CustomerId = _customerId, ProjectId = _projectId, Type = ContactType.Email, ContactedAt = DateTime.UtcNow.AddHours(-2) }
        };

        _customerRepoMock.Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _historyRepoMock
            .Setup(r => r.GetPagedByCustomerIdAsync(_customerId, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Domain.Entities.ContactHistory>)histories, 2));

        var query = new GetPagedContactHistoriesQuery { CustomerId = _customerId, Page = 1, PageSize = 20 };

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

        var query = new GetPagedContactHistoriesQuery { CustomerId = Guid.NewGuid(), Page = 1, PageSize = 20 };

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

        var query = new GetPagedContactHistoriesQuery { CustomerId = _customerId, Page = 1, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }
}
