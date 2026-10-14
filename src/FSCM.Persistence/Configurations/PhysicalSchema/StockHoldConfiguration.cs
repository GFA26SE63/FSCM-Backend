using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class StockHoldConfiguration : IEntityTypeConfiguration<StockHold>
{
    public void Configure(EntityTypeBuilder<StockHold> builder)
    {
        builder.ToTable("STOCK_HOLD");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.HeldAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReleasedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => entity.AllocationId).IsUnique();
        builder.HasOne<Allocation>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
