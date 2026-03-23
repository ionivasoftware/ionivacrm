using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="UserProjectRole"/> join entity.
/// Enforces a unique constraint: one user can have at most one role per project.
/// </summary>
public class UserProjectRoleConfiguration : IEntityTypeConfiguration<UserProjectRole>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserProjectRole> builder)
    {
        builder.ToTable("UserProjectRoles");

        builder.HasKey(r => r.Id);

        // Unique constraint: one active role per user per project
        builder.HasIndex(r => new { r.UserId, r.ProjectId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.Property(r => r.Role)
            .IsRequired();

        // FKs are defined in UserConfiguration and ProjectConfiguration
    }
}
