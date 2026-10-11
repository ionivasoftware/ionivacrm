using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core configuration for <see cref="SocialAccount"/> (connected social accounts, encrypted tokens).</summary>
public sealed class SocialAccountConfiguration : IEntityTypeConfiguration<SocialAccount>
{
    public void Configure(EntityTypeBuilder<SocialAccount> builder)
    {
        builder.ToTable("SocialAccounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Provider).IsRequired();
        builder.Property(a => a.Label).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Language).HasMaxLength(8);
        builder.Property(a => a.ExternalId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.ExternalName).HasMaxLength(200);
        builder.Property(a => a.ParentExternalId).HasMaxLength(100);
        builder.Property(a => a.AccessTokenEncrypted).IsRequired();
        builder.Property(a => a.Scopes).HasMaxLength(500);
        builder.Property(a => a.Status).IsRequired();
        builder.Property(a => a.ConnectedBy).HasMaxLength(320);

        builder.HasIndex(a => new { a.Provider, a.ExternalId }).IsUnique();
    }
}

/// <summary>EF Core configuration for <see cref="SocialPublishPost"/>.</summary>
public sealed class SocialPublishPostConfiguration : IEntityTypeConfiguration<SocialPublishPost>
{
    public void Configure(EntityTypeBuilder<SocialPublishPost> builder)
    {
        builder.ToTable("SocialPublishPosts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PostId).IsRequired();
        builder.Property(p => p.Topic).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Kind).IsRequired().HasMaxLength(32);

        builder.HasIndex(p => p.PostId).IsUnique();
    }
}

/// <summary>EF Core configuration for <see cref="SocialPublishJob"/> — unique per (post, account) so a platform is never posted twice.</summary>
public sealed class SocialPublishJobConfiguration : IEntityTypeConfiguration<SocialPublishJob>
{
    public void Configure(EntityTypeBuilder<SocialPublishJob> builder)
    {
        builder.ToTable("SocialPublishJobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.PostId).IsRequired();
        builder.Property(j => j.AccountId).IsRequired();
        builder.Property(j => j.Provider).IsRequired();
        builder.Property(j => j.Language).IsRequired().HasMaxLength(8);
        builder.Property(j => j.Status).IsRequired();
        builder.Property(j => j.PlatformPostId).HasMaxLength(200);
        builder.Property(j => j.ImageUrl).HasMaxLength(1000);

        builder.HasIndex(j => new { j.PostId, j.AccountId }).IsUnique();
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });
    }
}
