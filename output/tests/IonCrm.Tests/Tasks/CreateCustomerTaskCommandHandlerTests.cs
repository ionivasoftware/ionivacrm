using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Tasks.Commands.CreateCustomerTask;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Tasks;

public class CreateCustomerTaskCommandHandlerTests
{
    private readonly Mock<ICustomerTaskRepository> _taskRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<CreateCustomerTaskCommandHandler>> _loggerMock = new();

    private CreateCustomerTaskCommandHandler CreateHandler() => new(
        _taskRepoMock.Object,
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidCommand_CreatesTask()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test" };

        _customerRepoMock.Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });

        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CustomerTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerTask t, CancellationToken _) => t);

        var command = new CreateCustomerTaskCommand
        {
            CustomerId = _customerId,
            Title = "Send proposal",
            Priority = TaskPriority.High,
            DueDate = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CustomerId.Should().Be(_customerId);
        result.Value.Title.Should().Be("Send proposal");
        result.Value.Priority.Should().Be(TaskPriority.High);
        result.Value.Status.Should().Be(IonCrm.Domain.Enums.TaskStatus.Todo);
        result.Value.ProjectId.Should().Be(_projectId);
    }

    [Fact]
    public async Task Handle_DefaultStatus_IsTodo()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test" };

        _customerRepoMock.Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());

        _taskRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CustomerTask>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerTask t, CancellationToken _) => t);

        var command = new CreateCustomerTaskCommand
        {
            CustomerId = _customerId,
            Title = "Follow up"
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(IonCrm.Domain.Enums.TaskStatus.Todo);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new CreateCustomerTaskCommand
        {
            CustomerId = Guid.NewGuid(),
            Title = "Task"
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }
}
