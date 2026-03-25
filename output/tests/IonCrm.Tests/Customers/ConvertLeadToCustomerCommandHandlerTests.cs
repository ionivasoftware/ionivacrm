using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.ConvertLeadToCustomer;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

/// <summary>Unit tests for ConvertLeadToCustomerCommandHandler.</summary>
public class ConvertLeadToCustomerCommandHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<ConvertLeadToCustomerCommandHandler>> _loggerMock = new();

    private ConvertLeadToCustomerCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_LeadCustomer_ConvertsToActive()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = customerId,
            ProjectId = _projectId,
            CompanyName = "Lead Corp",
            Status = CustomerStatus.Lead
        };

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());

        _customerRepoMock.Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        _customerRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new ConvertLeadToCustomerCommand(customerId);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(CustomerStatus.Active);
        _customerRepoMock.Verify(r => r.UpdateAsync(
            It.Is<Customer>(c => c.Status == CustomerStatus.Active),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new ConvertLeadToCustomerCommand(Guid.NewGuid());

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
        _customerRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsAccessDenied()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = customerId,
            ProjectId = _projectId,
            CompanyName = "Lead Corp",
            Status = CustomerStatus.Lead
        };

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no projects

        _customerRepoMock.Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var command = new ConvertLeadToCustomerCommand(customerId);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _customerRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyActiveCustomer_ReturnsFailure()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = customerId,
            ProjectId = _projectId,
            CompanyName = "Active Corp",
            Status = CustomerStatus.Active // already active
        };

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var command = new ConvertLeadToCustomerCommand(customerId);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("already converted");
        _customerRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveCustomer_ReturnsFailure()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = customerId,
            ProjectId = _projectId,
            CompanyName = "Inactive Corp",
            Status = CustomerStatus.Inactive
        };

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock.Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        var command = new ConvertLeadToCustomerCommand(customerId);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Inactive");
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanConvertInAnyProject()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = customerId,
            ProjectId = Guid.NewGuid(), // some other project
            CompanyName = "SuperAdmin Lead",
            Status = CustomerStatus.Lead
        };

        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());

        _customerRepoMock.Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        _customerRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new ConvertLeadToCustomerCommand(customerId);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(CustomerStatus.Active);
    }
}
