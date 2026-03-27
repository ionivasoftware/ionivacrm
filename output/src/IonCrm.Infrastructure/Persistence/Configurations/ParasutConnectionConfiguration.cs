using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="ParasutConnection"/> entity.</summary>
public class ParasutConnectionConfiguration : IEntityTypeConfiguration<ParasutConnection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ParasutConnection> builder)
    {
        builder.ToTable("ParasutConnections");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ClientId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.ClientSecret)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(c => c.Username)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Password)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(c => c.AccessToken)
            .HasMaxLength(2000);

        builder.Property(c => c.RefreshToken)
            .HasMaxLength(2000);

        // One connection per project
        builder.HasIndex(c => c.ProjectId).IsUnique();

        // Ignore computed property — not a column
        builder.Ignore(c => c.IsConnected);

        builder.HasOne(c => c.Project)
            .WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
