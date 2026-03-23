using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="RefreshToken"/> entity.
/// Tokens are stored as SHA-256 hashes — never raw values.
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        // Indexed for fast lookup by token hash during refresh/logout
        builder.HasIndex(t => t.Token).IsUnique();

        builder.Property(t => t.Token)
            .IsRequired()
            .HasMaxLength(512); // SHA-256 hex = 64 chars; leaving room for future algorithms

        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.IsRevoked).IsRequired().HasDefaultValue(false);

        // Computed/derived — not mapped to DB columns
        builder.Ignore(t => t.IsExpired);
        builder.Ignore(t => t.IsActive);

        // FK: RefreshToken → User defined in UserConfiguration
    }
}
