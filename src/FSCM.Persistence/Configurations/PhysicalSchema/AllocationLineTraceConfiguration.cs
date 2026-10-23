using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationLineTraceConfiguration : IEntityTypeConfiguration<AllocationLineTrace>
{
    public void Configure(EntityTypeBuilder<AllocationLineTrace> builder)
    {
        builder.ToTable("ALLOCATION_LINE_TRACE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationPlanVersionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Outcome).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.OrderedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.DrawnQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ShortageQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.RequiredShelfLifeHours).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ShelfLifeWeight).HasPrecision(5, 4).IsRequired();
        builder.Property(entity => entity.DistanceWeight).HasPrecision(5, 4).IsRequired();
        builder.Property(entity => entity.CoverageWeight).HasPrecision(5, 4).IsRequired();
        builder.Property(entity => entity.MaximumDistanceMeters).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Note).HasMaxLength(500).IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.AllocationPlanVersionId, entity.AllocationRunLineId }).IsUnique();
        builder.HasOne<AllocationPlanVersion>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationRunLine>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
