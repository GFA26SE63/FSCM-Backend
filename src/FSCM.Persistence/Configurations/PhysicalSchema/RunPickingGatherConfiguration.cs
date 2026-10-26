using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RunPickingGatherConfiguration : IEntityTypeConfiguration<RunPickingGather>
{
    public void Configure(EntityTypeBuilder<RunPickingGather> builder)
    {
        builder.ToTable("RUN_PICKING_GATHER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RunPickingListId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SkuSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchCodeSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.RequiredQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.PickedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        // One row per batch on a list, so a picker makes one trip to each batch.
        builder.HasIndex(entity => new { entity.RunPickingListId, entity.BatchId }).IsUnique();
        builder.HasOne<RunPickingList>()
            .WithMany()
            .HasForeignKey(entity => entity.RunPickingListId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
