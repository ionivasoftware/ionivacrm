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

        // Keycloak tokens are long JWTs and the refresh token is stored encrypted (base64) — no length cap; columns are text.
        builder.Property(c => c.CompanyName).HasMaxLength(300);
        builder.Property(c => c.OAuthStateHash).HasMaxLength(64);
        // The OAuthStateHash index lives in Program.cs raw SQL like the other indexes of this table.

        // ProjectId is nullable — null means global connection.
        // Uniqueness is enforced via partial indexes in Program.cs (idempotent SQL).
        // EF does not own the index here to avoid conflicts with the raw-SQL schema.

        // Ignore computed property — not a column
        builder.Ignore(c => c.IsConnected);

        // FK is optional — global connections have no owning project.
        builder.HasOne(c => c.Project)
            .WithMany()
            .HasForeignKey(c => c.ProjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
