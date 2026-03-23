using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="Customer"/> entity.</summary>
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CompanyName)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(c => c.Code).HasMaxLength(50);
        builder.Property(c => c.ContactName).HasMaxLength(200);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.TaxNumber).HasMaxLength(50);
        builder.Property(c => c.TaxUnit).HasMaxLength(200);
        builder.Property(c => c.LegacyId).HasMaxLength(100);

        builder.Property(c => c.Status).IsRequired();

        // Composite index for efficient tenant-scoped searches
        builder.HasIndex(c => new { c.ProjectId, c.Status });
        builder.HasIndex(c => new { c.ProjectId, c.Email });
        builder.HasIndex(c => c.LegacyId); // Used by migration idempotency check

        // Navigation: Customer → ContactHistories (one-to-many)
        builder.HasMany(c => c.ContactHistories)
            .WithOne(h => h.Customer)
            .HasForeignKey(h => h.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Navigation: Customer → Tasks (one-to-many)
        builder.HasMany(c => c.Tasks)
            .WithOne(t => t.Customer)
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Navigation: Customer → Opportunities (one-to-many)
        builder.HasMany(c => c.Opportunities)
            .WithOne(o => o.Customer)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Navigation: Customer → AssignedUser (optional many-to-one)
        builder.HasOne(c => c.AssignedUser)
            .WithMany()
            .HasForeignKey(c => c.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
