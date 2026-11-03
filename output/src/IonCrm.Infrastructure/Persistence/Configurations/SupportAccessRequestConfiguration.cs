using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

public sealed class SupportAccessRequestConfiguration : IEntityTypeConfiguration<SupportAccessRequest>
{
    public void Configure(EntityTypeBuilder<SupportAccessRequest> builder)
    {
        builder.ToTable("SupportAccessRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.EmsUserId).IsRequired().HasMaxLength(100);
        builder.Property(r => r.EmsUserName).HasMaxLength(200);
        builder.Property(r => r.To).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(500);
        builder.Property(r => r.Token).IsRequired().HasMaxLength(64);
        builder.Property(r => r.RequestedBy).IsRequired().HasMaxLength(320);
        builder.Property(r => r.RequestedByName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.MessageId).HasMaxLength(300);
        builder.Property(r => r.SendError).HasMaxLength(1000);
        builder.Property(r => r.Language).HasMaxLength(8);
        builder.HasIndex(r => r.Token).IsUnique();
        builder.HasIndex(r => new { r.CustomerId, r.EmsUserId, r.RequestedAt });
    }
}
