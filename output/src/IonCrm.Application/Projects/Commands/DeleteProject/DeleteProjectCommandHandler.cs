using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;

namespace IonCrm.Application.Projects.Commands.DeleteProject;

/// <summary>Handles <see cref="DeleteProjectCommand"/> — soft-deletes (archives) a project.</summary>
public class DeleteProjectCommandHandler : IRequestHandler<DeleteProjectCommand, Result<string>>
{
    private readonly IProjectRepository _projectRepository;
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initialises a new instance of <see cref="DeleteProjectCommandHandler"/>.</summary>
    public DeleteProjectCommandHandler(IProjectRepository projectRepository, ICurrentUserService currentUser)
    {
        _projectRepository = projectRepository;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<Result<string>> Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsSuperAdmin)
            return Result<string>.Failure("Access denied. SuperAdmin required.");

        var project = await _projectRepository.GetByIdAsync(request.Id, cancellationToken);
        if (project is null)
            return Result<string>.Failure("Project not found (or already archived).");

        // Soft-delete only: the row and its dependent data stay in the DB, just hidden from the
        // project list. IsActive is also cleared so any code that reads IsActive sees it stopped.
        project.IsDeleted = true;
        project.IsActive  = false;

        await _projectRepository.UpdateAsync(project, cancellationToken);
        return Result<string>.Success($"'{project.Name}' projesi arşivlendi (proje seçicisinden kaldırıldı; veriler korundu).");
    }
}
