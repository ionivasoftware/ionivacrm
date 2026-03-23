using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="ContactHistory"/> entity.</summary>
public class ContactHistoryConfiguration : IEntityTypeConfiguration<ContactHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContactHistory> builder)
    {
        builder.ToTable("ContactHistories");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Subject).HasMaxLength(300);
        builder.Property(h => h.Content).HasMaxLength(4000);
        builder.Property(h => h.Outcome).HasMaxLength(500);
        builder.Property(h => h.LegacyId).HasMaxLength(100);
        builder.Property(h => h.Type).IsRequired();
        builder.Property(h => h.ContactedAt).IsRequired();

        builder.HasIndex(h => new { h.ProjectId, h.ContactedAt });
        builder.HasIndex(h => h.LegacyId);

        // Navigation: ContactHistory → Project (many-to-one, no reverse nav on Project)
        builder.HasOne(h => h.Project)
            .WithMany()
            .HasForeignKey(h => h.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Navigation: ContactHistory → CreatedByUser (optional many-to-one)
        builder.HasOne(h => h.CreatedByUser)
            .WithMany()
            .HasForeignKey(h => h.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
