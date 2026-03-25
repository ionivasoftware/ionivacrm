using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>Repository interface for <see cref="Project"/> (tenant) management.</summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Project> AddAsync(Project project, CancellationToken cancellationToken = default);
    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
}
