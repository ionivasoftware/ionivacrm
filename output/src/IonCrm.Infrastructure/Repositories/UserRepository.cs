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

        return await DbSet
            .Include(u => u.UserProjectRoles)
                .ThenInclude(upr => upr.Project)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
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
            .AsNoTracking()
            .Where(u => u.IsActive &&
                        u.UserProjectRoles.Any(upr =>
                            upr.ProjectId == projectId && !upr.IsDeleted))
            .ToListAsync(cancellationToken);
    }
}
