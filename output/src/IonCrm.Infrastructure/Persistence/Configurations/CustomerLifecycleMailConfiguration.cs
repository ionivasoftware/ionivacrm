using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

public sealed class CustomerLifecycleMailConfiguration : IEntityTypeConfiguration<CustomerLifecycleMail>
{
    public void Configure(EntityTypeBuilder<CustomerLifecycleMail> builder)
    {
        builder.ToTable("CustomerLifecycleMails");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Language).IsRequired().HasMaxLength(8);
        builder.Property(m => m.To).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Subject).IsRequired().HasMaxLength(500);
        builder.Property(m => m.MessageId).HasMaxLength(300);
        builder.Property(m => m.TriggeredBy).IsRequired().HasMaxLength(320);
        builder.HasIndex(m => new { m.CustomerId, m.Step });
        builder.HasIndex(m => m.SentAt);
    }
}
