using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RunPickingSplitConfiguration : IEntityTypeConfiguration<RunPickingSplit>
{
    public void Configure(EntityTypeBuilder<RunPickingSplit> builder)
    {
        builder.ToTable("RUN_PICKING_SPLIT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RunPickingGatherId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunAllocationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationPlanPriceId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OrderCodeSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.PickedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // One split per allocation: a quantity belongs to exactly one order line.
        builder.HasIndex(entity => entity.AllocationRunAllocationId).IsUnique();
        builder.HasOne<RunPickingGather>()
            .WithMany()
            .HasForeignKey(entity => entity.RunPickingGatherId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationRunAllocation>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AllocationPlanPrice>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationPlanPriceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
