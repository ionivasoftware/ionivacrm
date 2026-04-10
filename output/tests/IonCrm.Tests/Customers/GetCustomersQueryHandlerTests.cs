using IonCrm.Application.Customers.Queries.GetCustomers;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.Customers;

public class GetCustomersQueryHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();

    private GetCustomersQueryHandler CreateHandler() => new(_customerRepoMock.Object);

    [Fact]
    public async Task Handle_ReturnsPagedResult_WithCorrectMetadata()
    {
        // Arrange
        var customers = Enumerable.Range(1, 5).Select(i => new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            CompanyName = $"Company {i}",
            Status = CustomerStatus.Active
        }).ToList();

        _customerRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<CustomerStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<CustomerLabel?>(),
                It.IsAny<Guid?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<(Customer Customer, DateTime? LastActivityDate)>)customers.Select(c => (c, (DateTime?)null)).ToList(), 5));

        var query = new GetCustomersQuery { Page = 1, PageSize = 10 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(5);
        result.Value.TotalCount.Should().Be(5);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(10);
        result.Value.TotalPages.Should().Be(1);
        result.Value.HasNextPage.Should().BeFalse();
        result.Value.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PageSizeExceeds100_ClampedTo100()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetPagedAsync(
                null, null, null, null, null, null, 1, 100, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<(Customer Customer, DateTime? LastActivityDate)>)new List<(Customer, DateTime?)>(), 0));

        var query = new GetCustomersQuery { Page = 1, PageSize = 9999 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.GetPagedAsync(
            null, null, null, null, null, null, 1, 100, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PageBelowOne_ClampedToOne()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetPagedAsync(
                null, null, null, null, null, null, 1, 20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<(Customer Customer, DateTime? LastActivityDate)>)new List<(Customer, DateTime?)>(), 0));

        var query = new GetCustomersQuery { Page = -5, PageSize = 20 };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.GetPagedAsync(
            null, null, null, null, null, null, 1, 20, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
