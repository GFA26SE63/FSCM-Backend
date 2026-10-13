using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("BATCH");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ParentBatchId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RootBatchId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.GoodsReceiptLineId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.BatchCode).HasMaxLength(80).IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ExpirationDate).HasColumnType("date").IsRequired();
        builder.Property(entity => entity.ExpiryStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.StockStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Version).IsRequired().IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.BatchCode).IsUnique();
        builder.HasIndex(entity => new { entity.WarehouseId, entity.ProductId, entity.ExpirationDate }).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_BATCH_Quantity", "\"Quantity\" >= 0 AND TRUNC(\"Quantity\") = \"Quantity\""));
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.RootBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptLine>()
            .WithMany()
            .HasForeignKey(entity => entity.GoodsReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
