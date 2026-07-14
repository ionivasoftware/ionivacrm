using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="VendorInvoice"/> entity.</summary>
public sealed class VendorInvoiceConfiguration : IEntityTypeConfiguration<VendorInvoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VendorInvoice> builder)
    {
        builder.ToTable("VendorInvoices");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Provider).IsRequired().HasMaxLength(50);
        builder.Property(v => v.PeriodYear).IsRequired();
        builder.Property(v => v.PeriodMonth).IsRequired();
        builder.Property(v => v.BillingType).IsRequired();
        builder.Property(v => v.Status).IsRequired();

        builder.Property(v => v.ExpectedAmount).HasColumnType("numeric(18,2)");
        builder.Property(v => v.ReceivedAmount).HasColumnType("numeric(18,2)");
        builder.Property(v => v.Currency).HasMaxLength(10);
        builder.Property(v => v.InvoiceNumber).HasMaxLength(100);
        builder.Property(v => v.PdfUrl).HasMaxLength(1000);
        builder.Property(v => v.DueDay).IsRequired().HasDefaultValue(7);
        builder.Property(v => v.Notes).HasColumnType("text");

        // Idempotent upsert key — one row per provider per period.
        builder.HasIndex(v => new { v.Provider, v.PeriodYear, v.PeriodMonth }).IsUnique();
        builder.HasIndex(v => new { v.Status, v.PeriodYear, v.PeriodMonth });

        // DueDate() is a method, not a mapped property — EF ignores it automatically.
    }
}
