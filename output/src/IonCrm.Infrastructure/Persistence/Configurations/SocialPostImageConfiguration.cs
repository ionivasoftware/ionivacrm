using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="SocialPostImage"/> entity.</summary>
public sealed class SocialPostImageConfiguration : IEntityTypeConfiguration<SocialPostImage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SocialPostImage> builder)
    {
        builder.ToTable("SocialPostImages");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PostId).IsRequired();
        builder.Property(p => p.FileName).HasMaxLength(400);
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Content).HasColumnType("bytea");
        builder.Property(p => p.Kind).IsRequired().HasMaxLength(16).HasDefaultValue("Custom");
        builder.Property(p => p.Language).HasMaxLength(8);

        builder.HasIndex(p => p.PostId);
    }
}
