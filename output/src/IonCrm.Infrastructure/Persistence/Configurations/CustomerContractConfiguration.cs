using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="CustomerContract"/> entity.</summary>
public class CustomerContractConfiguration : IEntityTypeConfiguration<CustomerContract>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerContract> builder)
    {
        builder.ToTable("CustomerContracts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(c => c.MonthlyAmount)
            .HasPrecision(18, 2);

        builder.Property(c => c.PaymentType).IsRequired();
        builder.Property(c => c.Status).IsRequired();

        builder.Property(c => c.RezervalSubscriptionId).HasMaxLength(200);
        builder.Property(c => c.RezervalPaymentPlanId).HasMaxLength(200);

        // Indexes
        builder.HasIndex(c => new { c.ProjectId, c.CustomerId });
        builder.HasIndex(c => new { c.CustomerId, c.Status });
        // Composite for the background EFT-due query
        builder.HasIndex(c => new { c.Status, c.PaymentType, c.NextInvoiceDate });

        // Navigation: CustomerContract → Customer (many-to-one)
        builder.HasOne(c => c.Customer)
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Navigation: CustomerContract → Project (many-to-one)
        builder.HasOne(c => c.Project)
            .WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
