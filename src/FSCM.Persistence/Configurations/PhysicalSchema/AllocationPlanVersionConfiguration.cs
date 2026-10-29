using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationPlanVersionConfiguration : IEntityTypeConfiguration<AllocationPlanVersion>
{
    public void Configure(EntityTypeBuilder<AllocationPlanVersion> builder)
    {
        builder.ToTable("ALLOCATION_PLAN_VERSION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.VersionNumber).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.PricedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.FulfilledOrderCount).IsRequired();
        builder.Property(entity => entity.ShortOrderCount).IsRequired();
        builder.Property(entity => entity.TotalFinalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.ApprovedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.ApprovedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.RecalculationRequestedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RecalculationRequestedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.RecalculationReason).HasMaxLength(1_000).IsRequired(false);
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.AllocationRunId, entity.VersionNumber }).IsUnique();

        // At most one version of a run may be live at a time, so an approval can never be applied
        // to a proposal that a recalculation has already replaced.
        builder.HasIndex(entity => entity.AllocationRunId)
            .IsUnique()
            .HasFilter("\"Status\" = 'AwaitingApproval'");
        builder.HasOne<AllocationRun>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
