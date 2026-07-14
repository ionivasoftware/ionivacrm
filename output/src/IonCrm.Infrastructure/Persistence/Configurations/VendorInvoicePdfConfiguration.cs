using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core fluent configuration for the <see cref="VendorInvoicePdf"/> entity.</summary>
public sealed class VendorInvoicePdfConfiguration : IEntityTypeConfiguration<VendorInvoicePdf>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VendorInvoicePdf> builder)
    {
        builder.ToTable("VendorInvoicePdfs");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.VendorInvoiceId).IsRequired();
        builder.Property(p => p.FileName).HasMaxLength(400);
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Content).HasColumnType("bytea");

        builder.HasIndex(p => p.VendorInvoiceId).IsUnique();
    }
}
