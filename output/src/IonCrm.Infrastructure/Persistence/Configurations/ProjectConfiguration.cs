using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="Project"/> entity (tenant).</summary>
public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Navigation: Project → Customers (one-to-many)
        builder.HasMany(p => p.Customers)
            .WithOne(c => c.Project)
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Navigation: Project → SyncLogs (one-to-many)
        builder.HasMany(p => p.SyncLogs)
            .WithOne(s => s.Project)
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Navigation: Project → UserProjectRoles (one-to-many)
        builder.HasMany(p => p.UserProjectRoles)
            .WithOne(r => r.Project)
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
