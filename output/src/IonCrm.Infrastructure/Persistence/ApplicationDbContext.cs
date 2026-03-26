using IonCrm.Application.Common.Interfaces;
using IonCrm.Domain.Common;
using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IonCrm.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core DbContext for ION CRM.
/// Applies global query filters for soft-delete and multi-tenant isolation on every query.
/// SuperAdmin users bypass tenant filters (IsSuperAdmin = true means no ProjectId restriction).
/// </summary>
public class ApplicationDbContext : DbContext
{
    private readonly ICurrentUserService _currentUser;

    /// <summary>Initialises a new instance of <see cref="ApplicationDbContext"/>.</summary>
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService currentUser) : base(options)
    {
        _currentUser = currentUser;
    }

    // ── DbSets ────────────────────────────────────────────────────────────────
    /// <summary>Gets or sets the Users table.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Gets or sets the Projects (tenants) table.</summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>Gets or sets the UserProjectRoles join table.</summary>
    public DbSet<UserProjectRole> UserProjectRoles => Set<UserProjectRole>();

    /// <summary>Gets or sets the RefreshTokens table.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Gets or sets the Customers table.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Gets or sets the ContactHistories table.</summary>
    public DbSet<ContactHistory> ContactHistories => Set<ContactHistory>();

    /// <summary>Gets or sets the CustomerTasks table.</summary>
    public DbSet<CustomerTask> CustomerTasks => Set<CustomerTask>();

    /// <summary>Gets or sets the Opportunities table.</summary>
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    /// <summary>Gets or sets the SyncLogs table.</summary>
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();

    // ── Model configuration ───────────────────────────────────────────────────
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> classes in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // ── Global Query Filters ─────────────────────────────────────────────
        // Soft-delete filter — applies to ALL entities inheriting BaseEntity
        // Tenant filter — applies to entities with a ProjectId column

        modelBuilder.Entity<User>()
            .HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<Project>()
            .HasQueryFilter(e => !e.IsDeleted &&
                (_currentUser.IsSuperAdmin ||
                 _currentUser.ProjectIds.Contains(e.Id)));

        // UserProjectRole must NOT be filtered by project membership — it IS the table
        // that establishes membership. Filtering it by ProjectIds would prevent roles from
        // being loaded during login (when the user has no JWT yet), breaking all auth.
        modelBuilder.Entity<UserProjectRole>()
            .HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(e => !e.IsDeleted);

        modelBuilder.Entity<Customer>()
            .HasQueryFilter(e => !e.IsDeleted &&
                (_currentUser.IsSuperAdmin ||
                 _currentUser.ProjectIds.Contains(e.ProjectId)));

        modelBuilder.Entity<ContactHistory>()
            .HasQueryFilter(e => !e.IsDeleted &&
                (_currentUser.IsSuperAdmin ||
                 _currentUser.ProjectIds.Contains(e.ProjectId)));

        modelBuilder.Entity<CustomerTask>()
            .HasQueryFilter(e => !e.IsDeleted &&
                (_currentUser.IsSuperAdmin ||
                 _currentUser.ProjectIds.Contains(e.ProjectId)));

        modelBuilder.Entity<Opportunity>()
            .HasQueryFilter(e => !e.IsDeleted &&
                (_currentUser.IsSuperAdmin ||
                 _currentUser.ProjectIds.Contains(e.ProjectId)));

        modelBuilder.Entity<SyncLog>()
            .HasQueryFilter(e => !e.IsDeleted &&
                (_currentUser.IsSuperAdmin ||
                 _currentUser.ProjectIds.Contains(e.ProjectId)));
    }

    // ── Audit intercept ───────────────────────────────────────────────────────
    /// <summary>
    /// Overrides SaveChangesAsync to automatically update <see cref="BaseEntity.UpdatedAt"/>
    /// on every modified entity before persisting changes.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
