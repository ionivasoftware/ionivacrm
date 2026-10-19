using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core configuration for <see cref="LeadCandidate"/> — fingerprint unique.</summary>
public sealed class LeadCandidateConfiguration : IEntityTypeConfiguration<LeadCandidate>
{
    public void Configure(EntityTypeBuilder<LeadCandidate> builder)
    {
        builder.ToTable("LeadCandidates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CompanyName).IsRequired().HasMaxLength(300);
        builder.Property(c => c.NormalizedName).IsRequired().HasMaxLength(300);
        builder.Property(c => c.LegalTitle).HasMaxLength(400);
        builder.Property(c => c.Phone).HasMaxLength(60);
        builder.Property(c => c.NormalizedPhone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Website).HasMaxLength(500);
        builder.Property(c => c.Domain).HasMaxLength(200);
        builder.Property(c => c.Address).HasMaxLength(600);
        builder.Property(c => c.City).HasMaxLength(80);
        builder.Property(c => c.District).HasMaxLength(80);
        builder.Property(c => c.TaxNumber).HasMaxLength(30);
        builder.Property(c => c.MersisNo).HasMaxLength(30);
        builder.Property(c => c.Source).IsRequired().HasMaxLength(120);
        builder.Property(c => c.SourceUrl).HasMaxLength(1000);
        builder.Property(c => c.Fingerprint).IsRequired().HasMaxLength(64);
        builder.Property(c => c.AgentRunId).HasMaxLength(60);
        builder.Property(c => c.SearchCity).HasMaxLength(80);
        builder.Property(c => c.ReviewedBy).HasMaxLength(320);
        builder.Property(c => c.RejectReason).HasMaxLength(500);
        builder.HasIndex(c => c.Fingerprint).IsUnique();
        builder.HasIndex(c => new { c.Status, c.CreatedAt });
    }
}

/// <summary>EF Core configuration for <see cref="LeadAgentRun"/>.</summary>
public sealed class LeadAgentRunConfiguration : IEntityTypeConfiguration<LeadAgentRun>
{
    public void Configure(EntityTypeBuilder<LeadAgentRun> builder)
    {
        builder.ToTable("LeadAgentRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RunId).IsRequired().HasMaxLength(60);
        builder.Property(r => r.Cities).IsRequired().HasMaxLength(1000);
        builder.HasIndex(r => r.RunId).IsUnique();
    }
}
