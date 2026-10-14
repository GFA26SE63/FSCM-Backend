using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> builder)
    {
        builder.ToTable("ALLOCATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocatedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.AllocationSequence).IsRequired();
        builder.Property(entity => entity.DistanceMeters).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.EstimatedArrivalAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.RemainingLifeHoursAtArrival).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
