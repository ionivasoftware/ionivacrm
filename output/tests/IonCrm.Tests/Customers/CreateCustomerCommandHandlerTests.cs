using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Customers.Commands.CreateCustomer;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Customers;

public class CreateCustomerCommandHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<CreateCustomerCommandHandler>> _loggerMock = new();

    private CreateCustomerCommandHandler CreateHandler() => new(
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithCustomerDto()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = _projectId,
            CompanyName = "Acme Corp",
            ContactName = "John Doe",
            Email = "john@acme.com",
            Status = CustomerStatus.Lead
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.CompanyName.Should().Be("Acme Corp");
        result.Value.Email.Should().Be("john@acme.com");
        result.Value.ProjectId.Should().Be(_projectId);
        result.Value.Status.Should().Be(CustomerStatus.Lead);
    }

    [Fact]
    public async Task Handle_EmailIsNormalized_ToLowercase()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = _projectId,
            CompanyName = "Test Co",
            Email = "  TEST@EXAMPLE.COM  "
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no projects

        var command = new CreateCustomerCommand
        {
            ProjectId = _projectId,
            CompanyName = "Acme Corp"
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _customerRepoMock.Verify(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SuperAdmin_CanCreateInAnyProject()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no explicit projects

        _customerRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer c, CancellationToken _) => c);

        var command = new CreateCustomerCommand
        {
            ProjectId = Guid.NewGuid(), // arbitrary project
            CompanyName = "SuperAdmin Corp"
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
