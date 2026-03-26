using IonCrm.Application.Customers.Queries.GetCustomers;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Tests for the extended GetCustomers query with Sprint 2 filters:
/// ProjectId, Label, and backward-compatible existing filters.
/// </summary>
public class GetCustomersWithFiltersTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();

    private GetCustomersQueryHandler CreateHandler() => new(_customerRepoMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WithProjectIdFilter_PassesProjectIdToRepository()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetPagedAsync(
                _projectId, null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Customer>)new List<Customer>(), 0));

        var query = new GetCustomersQuery { ProjectId = _projectId };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.GetPagedAsync(
            _projectId, null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithLabelFilter_PassesLabelToRepository()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetPagedAsync(
                null, null, null, null, CustomerLabel.YuksekPotansiyel, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Customer>)new List<Customer>(), 0));

        var query = new GetCustomersQuery { Label = CustomerLabel.YuksekPotansiyel };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.GetPagedAsync(
            null, null, null, null, CustomerLabel.YuksekPotansiyel, null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithAllFilters_PassesAllFiltersToRepository()
    {
        // Arrange
        var assignedUserId = Guid.NewGuid();

        _customerRepoMock
            .Setup(r => r.GetPagedAsync(
                _projectId,
                "test search",
                CustomerStatus.Active,
                "Enterprise",
                CustomerLabel.Potansiyel,
                assignedUserId,
                2, 15,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<Customer>)new List<Customer>(), 0));

        var query = new GetCustomersQuery
        {
            ProjectId = _projectId,
            Search = "test search",
            Status = CustomerStatus.Active,
            Segment = "Enterprise",
            Label = CustomerLabel.Potansiyel,
            AssignedUserId = assignedUserId,
            Page = 2,
            PageSize = 15
        };

        // Act
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.GetPagedAsync(
            _projectId, "test search", CustomerStatus.Active, "Enterprise",
            CustomerLabel.Potansiyel, assignedUserId, 2, 15, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
