using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.ContactHistory.Commands.CreateContactHistory;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.ContactHistory;

public class CreateContactHistoryCommandHandlerTests
{
    private readonly Mock<IContactHistoryRepository> _historyRepoMock = new();
    private readonly Mock<ICustomerRepository> _customerRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<CreateContactHistoryCommandHandler>> _loggerMock = new();

    private CreateContactHistoryCommandHandler CreateHandler() => new(
        _historyRepoMock.Object,
        _customerRepoMock.Object,
        _currentUserMock.Object,
        _loggerMock.Object);

    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ValidCommand_CreatesContactHistory()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test" };

        _customerRepoMock.Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { _projectId });
        _currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());

        _historyRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Domain.Entities.ContactHistory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.ContactHistory h, CancellationToken _) => h);

        var command = new CreateContactHistoryCommand
        {
            CustomerId = _customerId,
            Type = ContactType.Call,
            Subject = "Follow-up call",
            Content = "Discussed renewal.",
            ContactedAt = DateTime.UtcNow.AddHours(-1)
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.CustomerId.Should().Be(_customerId);
        result.Value.Type.Should().Be(ContactType.Call);
        result.Value.Subject.Should().Be("Follow-up call");
        result.Value.ProjectId.Should().Be(_projectId);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsFailure()
    {
        // Arrange
        _customerRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var command = new CreateContactHistoryCommand
        {
            CustomerId = Guid.NewGuid(),
            Type = ContactType.Email,
            ContactedAt = DateTime.UtcNow
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_UserNotInProject_ReturnsFailure()
    {
        // Arrange
        var customer = new Customer { Id = _customerId, ProjectId = _projectId, CompanyName = "Test" };

        _customerRepoMock.Setup(r => r.GetByIdAsync(_customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>()); // no access

        var command = new CreateContactHistoryCommand
        {
            CustomerId = _customerId,
            Type = ContactType.Note,
            ContactedAt = DateTime.UtcNow
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
    }
}
