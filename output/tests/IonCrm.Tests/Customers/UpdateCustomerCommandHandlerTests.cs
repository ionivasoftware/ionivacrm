using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.UpdateCustomer;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

public class UpdateCustomerCommandHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<UpdateCustomerCommandHandler>> _loggerMock = new();

    private UpdateCustomerCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private Customer CreateCustomer(Guid? id = null, Guid? projectId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ProjectId = projectId ?? Guid.NewGuid(),
        CompanyName = "Original Corp",
        Email = "original@example.com",
        Status = CustomerStatus.Lead
    };

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new UpdateCustomerCommand { Id = Guid.NewGuid(), CompanyName = "Updated Corp" };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_ValidUpdate_ReturnsSuccessWithUpdatedDto()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customer = CreateCustomer(customerId, projectId);

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand
        {
            Id = customerId,
            CompanyName = "Updated Corp",
            ContactName = "New Contact",
            Status = CustomerStatus.Active
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("Updated Corp");
        result.Value.ContactName.Should().Be("New Contact");
        result.Value.Status.Should().Be(CustomerStatus.Active);
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsAccessDenied()
    {
        // Arrange
        var customer = CreateCustomer();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() }); // different project

        var command = new UpdateCustomerCommand { Id = customer.Id, CompanyName = "Updated Corp" };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanUpdateAnyCustomer()
    {
        // Arrange
        var customer = CreateCustomer();
        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        // ProjectIds does NOT contain the customer's project — but superadmin bypasses the check
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand { Id = customer.Id, CompanyName = "Admin Updated" };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("Admin Updated");
    }

    [Fact]
    public async Task Handle_EmailIsNormalized_ToLowercase()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = CreateCustomer(projectId: projectId);

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand
        {
            Id = customer.Id,
            CompanyName = "Test Corp",
            Email = "  UPPERCASE@EXAMPLE.COM  "
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("uppercase@example.com");
    }

    [Fact]
    public async Task Handle_UpdateCallsRepository()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customer = CreateCustomer(projectId: projectId);

        _customerRepoMock
            .Setup(r => r.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _customerRepoMock
            .Setup(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateCustomerCommand { Id = customer.Id, CompanyName = "Repo Check Corp" };

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        _customerRepoMock.Verify(
            r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
