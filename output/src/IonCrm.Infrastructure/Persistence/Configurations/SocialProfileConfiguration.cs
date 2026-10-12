using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core configuration for <see cref="SocialProfile"/> — one row per (provider, language).</summary>
public sealed class SocialProfileConfiguration : IEntityTypeConfiguration<SocialProfile>
{
    public void Configure(EntityTypeBuilder<SocialProfile> builder)
    {
        builder.ToTable("SocialProfiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider).IsRequired();
        builder.Property(p => p.Language).IsRequired().HasMaxLength(8);
        builder.Property(p => p.FieldsJson).IsRequired().HasColumnType("jsonb");
        builder.Property(p => p.UpdatedBy).HasMaxLength(320);

        builder.HasIndex(p => new { p.Provider, p.Language }).IsUnique();
    }
}
