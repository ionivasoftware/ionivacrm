using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _db;
    public ProjectRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _db.Projects
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Project> AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        _db.Projects.Update(project);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
