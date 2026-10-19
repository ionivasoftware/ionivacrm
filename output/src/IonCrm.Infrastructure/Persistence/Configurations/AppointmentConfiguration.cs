using IonCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IonCrm.Infrastructure.Persistence.Configurations;

/// <summary>EF Core configuration for <see cref="Appointment"/> (calendar) — relationships are model-only, the table is created by startup SQL.</summary>
public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ProjectId).IsRequired();
        builder.Property(a => a.CustomerId).IsRequired();
        builder.Property(a => a.Kind).IsRequired();
        builder.Property(a => a.Source).IsRequired();
        builder.Property(a => a.Status).IsRequired();
        builder.Property(a => a.ScheduledAt).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(200);
        builder.Property(a => a.SkipReason).HasMaxLength(300);

        builder.HasOne(a => a.Customer).WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.AssignedUser).WithMany().HasForeignKey(a => a.AssignedUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.ProjectId, a.ScheduledAt });
        builder.HasIndex(a => a.CustomerId);
    }
}

/// <summary>EF Core configuration for <see cref="CallPlanRun"/> — one row per (project, local plan date).</summary>
public sealed class CallPlanRunConfiguration : IEntityTypeConfiguration<CallPlanRun>
{
    public void Configure(EntityTypeBuilder<CallPlanRun> builder)
    {
        builder.ToTable("CallPlanRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ProjectId).IsRequired();
        builder.Property(r => r.PlanDate).IsRequired();
        builder.HasIndex(r => new { r.ProjectId, r.PlanDate }).IsUnique();
    }
}
