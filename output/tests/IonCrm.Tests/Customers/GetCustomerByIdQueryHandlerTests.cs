using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Queries.GetCustomerById;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.Customers;

/// <summary>
/// Tests for <see cref="GetCustomerByIdQueryHandler"/>.
/// Covers: found/not-found, tenant isolation (403 hidden as 404), SuperAdmin bypass.
/// </summary>
public class GetCustomerByIdQueryHandlerTests
{
    private readonly Mock<ICustomerRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();

    private GetCustomerByIdQueryHandler CreateHandler() => new(_repoMock.Object, _userMock.Object);

    [Fact]
    public async Task Handle_ExistingCustomer_UserInProject_ReturnsDto()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            CompanyName = "Found Corp",
            Email = "found@corp.com",
            Status = CustomerStatus.Active
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerByIdQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("Found Corp");
        result.Value.Email.Should().Be("found@corp.com");
        result.Value.Status.Should().Be(CustomerStatus.Active);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerByIdQuery(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_CustomerInOtherTenant_ReturnsNotFound_NotAccessDenied()
    {
        // Arrange — handler returns "not found" (not "access denied") to avoid leaking existence
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = projectB,
            CompanyName = "Secret Corp"
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectA });

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerByIdQuery(customer.Id), CancellationToken.None);

        // Assert — cross-tenant access is rejected and disguised as "not found"
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found",
            because: "handler must not reveal whether the customer exists in another tenant");
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanAccessAnyTenant()
    {
        // Arrange
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(), // arbitrary project
            CompanyName = "SuperAdmin Accessible"
        };

        _repoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _userMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _userMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        // Act
        var result = await CreateHandler().Handle(
            new GetCustomerByIdQuery(customer.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("SuperAdmin Accessible");
    }
}
