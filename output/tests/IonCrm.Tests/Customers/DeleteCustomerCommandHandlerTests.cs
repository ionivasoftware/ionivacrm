using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.DeleteCustomer;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

public class DeleteCustomerCommandHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<DeleteCustomerCommandHandler>> _loggerMock = new();

    private DeleteCustomerCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    [Fact]
    public async Task Handle_ExistingCustomer_SoftDeletes()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, ProjectId = projectId, CompanyName = "Test" };

        _customerRepoMock.Setup(r => r.GetByIdAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _customerRepoMock.Setup(r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateHandler().Handle(new DeleteCustomerCommand(customerId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _customerRepoMock.Verify(r => r.DeleteAsync(customer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        // Act
        var result = await CreateHandler().Handle(new DeleteCustomerCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }
}
