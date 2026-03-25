using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.ContactHistory.Commands.DeleteContactHistory;
using IonCrm.Application.ContactHistory.Commands.UpdateContactHistory;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Enums;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

// Explicit alias to avoid namespace conflict with the test folder name
using ContactHistoryEntity = IonCrm.Domain.Entities.ContactHistory;

namespace IonCrm.Tests.ContactHistory;

/// <summary>
/// Tests for UpdateContactHistoryCommandHandler and DeleteContactHistoryCommandHandler.
/// Covers: success path, not-found, access denied, superadmin bypass, and soft-delete.
/// </summary>
public class UpdateDeleteContactHistoryCommandHandlerTests
{
    private readonly Mock<IContactHistoryRepository> _repoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private readonly Mock<ILogger<UpdateContactHistoryCommandHandler>> _updateLoggerMock = new();
    private readonly Mock<ILogger<DeleteContactHistoryCommandHandler>> _deleteLoggerMock = new();

    private UpdateContactHistoryCommandHandler CreateUpdateHandler() => new(
        _repoMock.Object, _currentUserMock.Object, _updateLoggerMock.Object);

    private DeleteContactHistoryCommandHandler CreateDeleteHandler() => new(
        _repoMock.Object, _currentUserMock.Object, _deleteLoggerMock.Object);

    private ContactHistoryEntity MakeHistory(Guid? projectId = null) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId ?? Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Type = ContactType.Call,
        Subject = "Original Subject",
        Content = "Original Content",
        Outcome = "Original Outcome",
        ContactedAt = DateTime.UtcNow.AddDays(-1)
    };

    // ── UpdateContactHistory ──────────────────────────────────────────────────

    [Fact]
    public async Task Update_NotFound_ReturnsFailure()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContactHistoryEntity?)null);

        var command = new UpdateContactHistoryCommand
        {
            Id = Guid.NewGuid(), Type = ContactType.Email,
            ContactedAt = DateTime.UtcNow
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Update_UserNotInProject_ReturnsAccessDenied()
    {
        // Arrange
        var history = MakeHistory(Guid.NewGuid());
        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() }); // different project

        var command = new UpdateContactHistoryCommand
        {
            Id = history.Id, Type = ContactType.Email, ContactedAt = DateTime.UtcNow
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _repoMock.Verify(r => r.UpdateAsync(It.IsAny<ContactHistoryEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_AuthorizedUser_AllFieldsUpdated()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var history = MakeHistory(projectId);

        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _repoMock.Setup(r => r.UpdateAsync(history, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var newDate = DateTime.UtcNow;
        var command = new UpdateContactHistoryCommand
        {
            Id = history.Id,
            Type = ContactType.Meeting,
            Subject = "Updated Subject",
            Content = "Updated Content",
            Outcome = "Updated Outcome",
            ContactedAt = newDate
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        history.Type.Should().Be(ContactType.Meeting);
        history.Subject.Should().Be("Updated Subject");
        history.Content.Should().Be("Updated Content");
        history.Outcome.Should().Be("Updated Outcome");
        history.ContactedAt.Should().Be(newDate);
    }

    [Fact]
    public async Task Update_SuperAdmin_CanUpdateAnyProject()
    {
        // Arrange
        var history = MakeHistory(Guid.NewGuid());

        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _repoMock.Setup(r => r.UpdateAsync(history, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateContactHistoryCommand
        {
            Id = history.Id, Type = ContactType.Note,
            Subject = "Admin Update", ContactedAt = DateTime.UtcNow
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Subject.Should().Be("Admin Update");
    }

    [Fact]
    public async Task Update_ValidCommand_ReturnsUpdatedDto()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var history = MakeHistory(projectId);

        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _repoMock.Setup(r => r.UpdateAsync(history, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateContactHistoryCommand
        {
            Id = history.Id, Type = ContactType.Email,
            Subject = "Meeting Notes", ContactedAt = DateTime.UtcNow
        };

        // Act
        var result = await CreateUpdateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(history.Id);
        result.Value.Type.Should().Be(ContactType.Email);
    }

    // ── DeleteContactHistory ──────────────────────────────────────────────────

    [Fact]
    public async Task Delete_NotFound_ReturnsFailure()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContactHistoryEntity?)null);

        // Act
        var result = await CreateDeleteHandler().Handle(
            new DeleteContactHistoryCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    [Fact]
    public async Task Delete_UserNotInProject_ReturnsAccessDenied()
    {
        // Arrange
        var history = MakeHistory(Guid.NewGuid());
        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { Guid.NewGuid() });

        // Act
        var result = await CreateDeleteHandler().Handle(
            new DeleteContactHistoryCommand(history.Id), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("Access denied");
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<ContactHistoryEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_AuthorizedUser_CallsDeleteAsyncAndReturnsSuccess()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var history = MakeHistory(projectId);

        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectId });
        _repoMock.Setup(r => r.DeleteAsync(history, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateDeleteHandler().Handle(
            new DeleteContactHistoryCommand(history.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repoMock.Verify(r => r.DeleteAsync(history, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_SuperAdmin_CanDeleteAnyProject()
    {
        // Arrange
        var history = MakeHistory(Guid.NewGuid());

        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid>());
        _repoMock.Setup(r => r.DeleteAsync(history, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await CreateDeleteHandler().Handle(
            new DeleteContactHistoryCommand(history.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repoMock.Verify(r => r.DeleteAsync(history, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_CrossTenant_DeleteNeverCalled()
    {
        // Arrange — user in Project A tries to delete Project B history
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var history = MakeHistory(projectB);

        _repoMock.Setup(r => r.GetByIdAsync(history.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentUserMock.Setup(u => u.ProjectIds).Returns(new List<Guid> { projectA });

        // Act
        var result = await CreateDeleteHandler().Handle(
            new DeleteContactHistoryCommand(history.Id), CancellationToken.None);

        // Assert — cross-tenant delete blocked, repo.Delete never called
        result.IsFailure.Should().BeTrue();
        _repoMock.Verify(
            r => r.DeleteAsync(It.IsAny<ContactHistoryEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
