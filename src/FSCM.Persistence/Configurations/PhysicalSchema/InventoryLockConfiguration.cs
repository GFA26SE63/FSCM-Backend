using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class InventoryLockConfiguration : IEntityTypeConfiguration<InventoryLock>
{
    public void Configure(EntityTypeBuilder<InventoryLock> builder)
    {
        builder.ToTable("INVENTORY_LOCK");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LockedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Reason).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.RequiresResolution).IsRequired();
        builder.Property(entity => entity.LockedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.ReleasedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.ReleasedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Version).IsRequired().IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.BatchId);
        builder.HasIndex(entity => new { entity.WarehouseId, entity.Status });
        builder.HasIndex(entity => new { entity.BatchId, entity.Status });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_INVENTORY_LOCK_LockedQuantity", "\"LockedQuantity\" > 0 AND TRUNC(\"LockedQuantity\") = \"LockedQuantity\""));
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.LockedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
