using IonCrm.Domain.Entities;

namespace IonCrm.Domain.Interfaces;

/// <summary>
/// Repository contract for User-specific data access operations.
/// Extends the generic IRepository with auth and tenant-related queries.
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>Finds a user by email address (case-insensitive). Returns null if not found.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Gets a user by ID including their UserProjectRole assignments and related projects.</summary>
    Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Checks whether an email address is already registered (for uniqueness validation).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Gets all non-deleted, active users who are members of a specific project.</summary>
    Task<IReadOnlyList<User>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Gets all users with their project-role assignments and related project data loaded.</summary>
    Task<IReadOnlyList<User>> GetAllWithRolesAsync(CancellationToken cancellationToken = default);
}
