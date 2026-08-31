using System.Linq.Expressions;
using IonCrm.Application.Auth.Commands.RemoveRole;
using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace IonCrm.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="RemoveRoleCommandHandler"/> — soft-delete of a user's role in a
/// single project (used to fix a user who is assigned to more projects than intended).
/// </summary>
public class RemoveRoleCommandHandlerTests
{
    private readonly Mock<IRepository<UserProjectRole>> _roleRepoMock = new();

    private RemoveRoleCommandHandler CreateHandler() =>
        new(_roleRepoMock.Object, Mock.Of<ILogger<RemoveRoleCommandHandler>>());

    [Fact]
    public async Task Handle_ActiveRoleExists_SoftDeletesItViaUpdate()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var role = new UserProjectRole { UserId = userId, ProjectId = projectId, IsDeleted = false };
        UserProjectRole? updated = null;

        _roleRepoMock
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<UserProjectRole, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProjectRole> { role });
        _roleRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<UserProjectRole>(), It.IsAny<CancellationToken>()))
            .Callback<UserProjectRole, CancellationToken>((r, _) => updated = r)
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new RemoveRoleCommand(userId, projectId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.IsDeleted.Should().BeTrue("removing a project role soft-deletes the membership");
        _roleRepoMock.Verify(r => r.UpdateAsync(It.IsAny<UserProjectRole>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoActiveRole_ReturnsFailure_AndDoesNotUpdate()
    {
        _roleRepoMock
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<UserProjectRole, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProjectRole>());

        var result = await CreateHandler().Handle(
            new RemoveRoleCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _roleRepoMock.Verify(r => r.UpdateAsync(It.IsAny<UserProjectRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
