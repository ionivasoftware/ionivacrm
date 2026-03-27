using IonCrm.Domain.Entities;
using IonCrm.Domain.Interfaces;
using IonCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Repositories;

/// <summary>
/// EF Core repository for <see cref="User"/> with auth-specific query helpers.
/// Soft-delete global query filter is applied by <see cref="ApplicationDbContext"/>.
/// </summary>
public class UserRepository : GenericRepository<User>, IUserRepository
{
    /// <summary>Initialises a new instance of <see cref="UserRepository"/>.</summary>
    public UserRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToLowerInvariant();
        return await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalised, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> GetByIdWithRolesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            return null;

        // ThenInclude(Project) cannot be used here: Project is non-nullable so EF Core
        // generates an INNER JOIN. The Project global filter (ProjectIds.Contains) returns
        // nothing during login (no JWT yet), making the join eliminate all role rows.
        // Solution: load roles without the join, then fetch project names separately
        // using IgnoreQueryFilters() so the soft-delete-only filter applies.
        var user = await DbSet
            .AsNoTracking()
            .Include(u => u.UserProjectRoles.Where(upr => !upr.IsDeleted))
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null || user.UserProjectRoles.Count == 0)
            return user;

        var projectIds = user.UserProjectRoles.Select(r => r.ProjectId).ToList();
        var projects = await Context.Projects
            .IgnoreQueryFilters()
            .Where(p => projectIds.Contains(p.Id) && !p.IsDeleted)
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var role in user.UserProjectRoles)
            role.Project = projects.GetValueOrDefault(role.ProjectId)!;

        return user;
    }

    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToLowerInvariant();
        return await DbSet
            .AnyAsync(u => u.Email == normalised, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(u => u.UserProjectRoles.Where(upr => !upr.IsDeleted))
                .ThenInclude(upr => upr.Project)
            .AsNoTracking()
            .Where(u => u.IsActive &&
                        u.UserProjectRoles.Any(upr =>
                            upr.ProjectId == projectId && !upr.IsDeleted))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns all users with their project-role assignments loaded.</summary>
    public async Task<IReadOnlyList<User>> GetAllWithRolesAsync(
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(u => u.UserProjectRoles.Where(upr => !upr.IsDeleted))
                .ThenInclude(upr => upr.Project)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
