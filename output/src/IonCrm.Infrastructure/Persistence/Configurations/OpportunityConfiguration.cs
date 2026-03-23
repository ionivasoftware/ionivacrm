using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="Opportunity"/> entity.</summary>
public class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Opportunity> builder)
    {
        builder.ToTable("Opportunities");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Title)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(o => o.Value)
            .HasColumnType("numeric(18,2)");

        builder.Property(o => o.Stage).IsRequired();

        builder.Property(o => o.Probability)
            .HasDefaultValue(null);

        builder.HasIndex(o => new { o.ProjectId, o.Stage });

        // Navigation: Opportunity → Project (many-to-one, no reverse nav on Project)
        builder.HasOne(o => o.Project)
            .WithMany()
            .HasForeignKey(o => o.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Navigation: Opportunity → AssignedUser (optional)
        builder.HasOne(o => o.AssignedUser)
            .WithMany()
            .HasForeignKey(o => o.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
