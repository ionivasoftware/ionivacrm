using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Queries.GetCustomerWithDetails;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using ContactHistoryEntity = IonCrm.Domain.Entities.ContactHistory;

namespace IonCrm.Tests.Customers;

/// <summary>Unit tests for GetCustomerWithDetailsQueryHandler.</summary>
public class GetCustomerWithDetailsQueryHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private GetCustomerWithDetailsQueryHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();

    private static Customer BuildCustomerWithDetails(Guid projectId)
    {
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = customerId,
            ProjectId = projectId,
            CompanyName = "Detail Corp",
            Status = CustomerStatus.Active,
            Label = CustomerLabel.YuksekPotansiyel
        };

        customer.ContactHistories = new List<ContactHistoryEntity>
        {
            new() { Id = Guid.NewGuid(), CustomerId = customerId, ProjectId = projectId,
                    Type = ContactType.Call, ContactedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), CustomerId = customerId, ProjectId = projectId,
                    Type = ContactType.Email, ContactedAt = DateTime.UtcNow.AddDays(-2) },
        };

        customer.Tasks = new List<CustomerTask>
        {
            new() { Id = Guid.NewGuid(), CustomerId = customerId, ProjectId = projectId,
                    Title = "Open Task", Status = IonCrm.Domain.Enums.TaskStatus.Todo },
            new() { Id = Guid.NewGuid(), CustomerId = customerId, ProjectId = projectId,
                    Title = "Done Task", Status = IonCrm.Domain.Enums.TaskStatus.Done },
        };

        return customer;
    }

    [Fact]
    public async Task Handle_ValidCustomer_ReturnsDetailsWithCounts()
    {
        // Arrange
        var customer = BuildCustomerWithDetails(_projectId);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.GetWithDetailsAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerWithDetailsQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.CompanyName.Should().Be("Detail Corp");
        dto.Label.Should().Be(CustomerLabel.YuksekPotansiyel);
        dto.TotalContactHistories.Should().Be(2);
        dto.TotalTasks.Should().Be(2);
        dto.OpenTasksCount.Should().Be(1); // Only Todo/InProgress tasks
        dto.RecentContactHistories.Should().HaveCount(2);
        dto.OpenTasks.Should().HaveCount(1);
        dto.OpenTasks[0].Title.Should().Be("Open Task");
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerWithDetailsQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsNotFound()
    {
        // Arrange
        var customer = BuildCustomerWithDetails(_projectId);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // different project

        _customerRepoMock.Setup(r => r.GetWithDetailsAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerWithDetailsQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanGetAnyCustomerDetails()
    {
        // Arrange
        var customer = BuildCustomerWithDetails(_projectId);

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        _customerRepoMock.Setup(r => r.GetWithDetailsAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerWithDetailsQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("Detail Corp");
    }

    [Fact]
    public async Task Handle_RecentHistories_LimitedToFive()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = customerId,
            ProjectId = _projectId,
            CompanyName = "History Corp",
            Status = CustomerStatus.Active
        };

        // Create 7 contact history records
        customer.ContactHistories = Enumerable.Range(1, 7).Select(i => new ContactHistoryEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ProjectId = _projectId,
            Type = ContactType.Call,
            ContactedAt = DateTime.UtcNow.AddDays(-i)
        }).ToList();

        customer.Tasks = new List<CustomerTask>();

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.GetWithDetailsAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        // Act
        var result = await CreateHandler().Handle(new GetCustomerWithDetailsQuery(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalContactHistories.Should().Be(7);
        result.Value.RecentContactHistories.Should().HaveCount(5); // capped at 5
    }
}
