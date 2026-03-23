using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="CustomerTask"/> entity.</summary>
public class CustomerTaskConfiguration : IEntityTypeConfiguration<CustomerTask>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerTask> builder)
    {
        builder.ToTable("CustomerTasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.Priority).IsRequired();
        builder.Property(t => t.Status).IsRequired();

        builder.HasIndex(t => new { t.ProjectId, t.Status });
        builder.HasIndex(t => new { t.ProjectId, t.AssignedUserId });

        // Navigation: CustomerTask → Project (many-to-one, no reverse nav on Project)
        builder.HasOne(t => t.Project)
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Navigation: CustomerTask → AssignedUser (optional)
        builder.HasOne(t => t.AssignedUser)
            .WithMany()
            .HasForeignKey(t => t.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
