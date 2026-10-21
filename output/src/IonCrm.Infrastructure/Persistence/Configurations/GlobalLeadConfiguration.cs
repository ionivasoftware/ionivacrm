using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

public sealed class GlobalLeadConfiguration : IEntityTypeConfiguration<GlobalLead>
{
    public void Configure(EntityTypeBuilder<GlobalLead> builder)
    {
        builder.ToTable("GlobalLeads");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Country).IsRequired().HasMaxLength(2);
        builder.Property(l => l.Region).HasMaxLength(120);
        builder.Property(l => l.CompanyName).IsRequired().HasMaxLength(300);
        builder.Property(l => l.NormalizedName).IsRequired().HasMaxLength(300);
        builder.Property(l => l.ContactName).HasMaxLength(200);
        builder.Property(l => l.Email).HasMaxLength(200);
        builder.Property(l => l.Domain).HasMaxLength(200);
        builder.Property(l => l.Phone).HasMaxLength(60);
        builder.Property(l => l.NormalizedPhone).HasMaxLength(20);
        builder.Property(l => l.Website).HasMaxLength(500);
        builder.Property(l => l.Address).HasMaxLength(600);
        builder.Property(l => l.City).HasMaxLength(120);
        builder.Property(l => l.Source).IsRequired().HasMaxLength(120);
        builder.Property(l => l.SourceUrl).HasMaxLength(1000);
        builder.Property(l => l.Fingerprint).IsRequired().HasMaxLength(64);
        builder.Property(l => l.AgentRunId).HasMaxLength(60);
        builder.Property(l => l.ReviewedBy).HasMaxLength(320);
        builder.Property(l => l.RejectReason).HasMaxLength(500);
        builder.Property(l => l.ThreadMessageId).HasMaxLength(300);
        builder.Property(l => l.UnsubscribeToken).IsRequired().HasMaxLength(64);
        builder.HasIndex(l => l.Fingerprint).IsUnique();
        builder.HasIndex(l => l.UnsubscribeToken).IsUnique();
        builder.HasIndex(l => new { l.Status, l.OutreachStatus, l.NextSendAt });
        builder.HasIndex(l => l.Country);
    }
}

public sealed class GlobalLeadEmailConfiguration : IEntityTypeConfiguration<GlobalLeadEmail>
{
    public void Configure(EntityTypeBuilder<GlobalLeadEmail> builder)
    {
        builder.ToTable("GlobalLeadEmails");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TemplateKey).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Subject).IsRequired().HasMaxLength(500);
        builder.Property(e => e.To).IsRequired().HasMaxLength(200);
        builder.Property(e => e.MessageId).HasMaxLength(300);
        builder.HasIndex(e => e.LeadId);
        builder.HasIndex(e => new { e.SenderUserId, e.SentAt });
    }
}
