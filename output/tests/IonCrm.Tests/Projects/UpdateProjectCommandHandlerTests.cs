using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Projects.Commands.UpdateProject;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.Projects;

/// <summary>
/// Unit tests for <see cref="UpdateProjectCommandHandler"/>.
/// Covers: SuperAdmin-only authorization, field updates, EMS/RezervAl API key nullable handling.
/// </summary>
public class UpdateProjectCommandHandlerTests
{
    private readonly Mock<IProjectRepository> _projectRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    private static readonly Guid _projectId = Guid.NewGuid();

    private UpdateProjectCommandHandler CreateHandler() => new(
        _projectRepoMock.Object,
        _currentUserMock.Object);

    private void SetupSuperAdmin()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
    }

    private Project CreateProject(string name = "Test Proje") => new()
    {
        Id = _projectId,
        Name = name,
        Description = "Eski açıklama",
        IsActive = true,
        EmsBaseUrl = "https://ems.example.com",
        EmsApiKey = "old-ems-key",
        RezervAlBaseUrl = "https://rezerval.example.com",
        RezervAlApiKey = "old-rezerval-key"
    };

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NotSuperAdmin_ReturnsFailure()
    {
        // Arrange
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);

        var command = new UpdateProjectCommand(_projectId, "Yeni İsim", null, true);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("SuperAdmin");
        _projectRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsFailure()
    {
        // Arrange
        SetupSuperAdmin();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var command = new UpdateProjectCommand(_projectId, "Ad", null, true);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Contain("not found");
    }

    // ── Core field updates ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_UpdatesNameDescriptionAndIsActive()
    {
        // Arrange
        SetupSuperAdmin();
        var project = CreateProject();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateProjectCommand(
            _projectId, "  Güncel Proje  ", "Yeni açıklama", false);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Güncel Proje");   // trimmed
        result.Value.Description.Should().Be("Yeni açıklama");
        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NullDescription_SetsDescriptionToNull()
    {
        // Arrange
        SetupSuperAdmin();
        var project = CreateProject();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateProjectCommand(_projectId, "Proje", null, true);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.Value!.Description.Should().BeNull();
    }

    // ── EMS / RezervAl API key handling ──────────────────────────────────────

    [Fact]
    public async Task Handle_EmsBaseUrl_Provided_UpdatesEmsBaseUrl()
    {
        // Arrange
        SetupSuperAdmin();
        var project = CreateProject();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateProjectCommand(
            _projectId, "P", null, true,
            EmsBaseUrl: "https://new-ems.example.com",
            EmsApiKey: "new-ems-key");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.EmsBaseUrl.Should().Be("https://new-ems.example.com");
        result.Value.EmsApiKey.Should().Be("new-ems-key");
    }

    [Fact]
    public async Task Handle_EmptyStringEmsApiKey_SetsKeyToNull()
    {
        // Arrange — empty/whitespace key should be treated as "clear the key"
        SetupSuperAdmin();
        var project = CreateProject();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateProjectCommand(
            _projectId, "P", null, true,
            EmsApiKey: "   ");  // whitespace → null

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.Value!.EmsApiKey.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NullEmsApiKeyParameter_DoesNotChangeExistingKey()
    {
        // Arrange — null parameter means "don't touch the field"
        SetupSuperAdmin();
        var project = CreateProject(); // has EmsApiKey = "old-ems-key"
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // null EmsApiKey → field should NOT be touched
        var command = new UpdateProjectCommand(_projectId, "P", null, true, EmsApiKey: null);

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert — existing key preserved
        result.Value!.EmsApiKey.Should().Be("old-ems-key");
    }

    [Fact]
    public async Task Handle_RezervAlApiKey_Provided_UpdatesKey()
    {
        // Arrange
        SetupSuperAdmin();
        var project = CreateProject();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateProjectCommand(
            _projectId, "P", null, true,
            RezervAlApiKey: "new-rezerval-key");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.Value!.RezervAlApiKey.Should().Be("new-rezerval-key");
    }

    [Fact]
    public async Task Handle_EmptyRezervAlApiKey_SetsKeyToNull()
    {
        // Arrange
        SetupSuperAdmin();
        var project = CreateProject();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateProjectCommand(
            _projectId, "P", null, true,
            RezervAlApiKey: "");

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        result.Value!.RezervAlApiKey.Should().BeNull();
    }

    // ── UpdateAsync called once ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_CallsUpdateAsyncExactlyOnce()
    {
        // Arrange
        SetupSuperAdmin();
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProject());
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new UpdateProjectCommand(_projectId, "Ad", null, true);

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        _projectRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
