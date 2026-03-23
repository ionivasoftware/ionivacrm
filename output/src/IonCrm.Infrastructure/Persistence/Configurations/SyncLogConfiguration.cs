using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="SyncLog"/> entity.</summary>
public class SyncLogConfiguration : IEntityTypeConfiguration<SyncLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SyncLog> builder)
    {
        builder.ToTable("SyncLogs");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.EntityId).HasMaxLength(100);
        builder.Property(s => s.ErrorMessage).HasMaxLength(2000);
        builder.Property(s => s.Payload).HasColumnType("text");

        builder.Property(s => s.Source).IsRequired();
        builder.Property(s => s.Direction).IsRequired();
        builder.Property(s => s.Status).IsRequired();

        builder.HasIndex(s => new { s.ProjectId, s.Status, s.CreatedAt });
        builder.HasIndex(s => new { s.Source, s.Status });
    }
}
