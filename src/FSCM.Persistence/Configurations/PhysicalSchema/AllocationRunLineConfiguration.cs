using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class AllocationRunLineConfiguration : IEntityTypeConfiguration<AllocationRunLine>
{
    public void Configure(EntityTypeBuilder<AllocationRunLine> builder)
    {
        builder.ToTable("ALLOCATION_RUN_LINE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AllocationRunOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderLineId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SkuSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.LineNumber).IsRequired();
        builder.Property(entity => entity.OrderedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.TotalShelfLifeHours).IsRequired();
        builder.Property(entity => entity.EffectiveMlorRatio).HasPrecision(5, 4).IsRequired();
        builder.Property(entity => entity.MlorSource).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.BaseNetUnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.AllocatedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.AllocationRunOrderId, entity.SalesOrderLineId }).IsUnique();
        builder.HasOne<AllocationRunOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.AllocationRunOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
