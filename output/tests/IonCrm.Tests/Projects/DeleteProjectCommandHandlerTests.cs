using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Projects.Commands.DeleteProject;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;

namespace IonCrm.Tests.Projects;

/// <summary>
/// Unit tests for <see cref="DeleteProjectCommandHandler"/> — soft-delete (archive) of a project.
/// Covers: SuperAdmin-only authorization, not-found handling, and that it soft-deletes
/// (IsDeleted=true + IsActive=false) via UpdateAsync rather than a hard delete.
/// </summary>
public class DeleteProjectCommandHandlerTests
{
    private readonly Mock<IProjectRepository> _projectRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();
    private static readonly Guid _projectId = Guid.NewGuid();

    private DeleteProjectCommandHandler CreateHandler() => new(
        _projectRepoMock.Object,
        _currentUserMock.Object);

    [Fact]
    public async Task Handle_NotSuperAdmin_ReturnsFailure_AndDoesNotTouchRepo()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);

        var result = await CreateHandler().Handle(new DeleteProjectCommand(_projectId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _projectRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsFailure()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var result = await CreateHandler().Handle(new DeleteProjectCommand(_projectId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _projectRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SoftDeletes_SetsIsDeletedAndIsActive_ViaUpdate()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);
        var project = new Project { Id = _projectId, Name = "EMS", IsActive = true, IsDeleted = false };
        Project? updated = null;
        _projectRepoMock
            .Setup(r => r.GetByIdAsync(_projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        _projectRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
            .Callback<Project, CancellationToken>((p, _) => updated = p)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new DeleteProjectCommand(_projectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.IsDeleted.Should().BeTrue("archive is a soft-delete");
        updated.IsActive.Should().BeFalse("an archived project is also marked inactive");
        // Soft-delete, not hard-delete: no Delete/Remove call, only UpdateAsync.
        _projectRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
