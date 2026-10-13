using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("INVENTORY_BALANCE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PhysicalQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.HeldQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ReservedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.AvailableQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Version).IsRequired().IsConcurrencyToken();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<Batch>()
            .WithOne()
            .HasForeignKey<InventoryBalance>(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_INVENTORY_BALANCE_PhysicalQuantity", "\"PhysicalQuantity\" >= 0");
            table.HasCheckConstraint("CK_INVENTORY_BALANCE_HeldQuantity", "\"HeldQuantity\" >= 0");
            table.HasCheckConstraint("CK_INVENTORY_BALANCE_ReservedQuantity", "\"ReservedQuantity\" >= 0");
            table.HasCheckConstraint("CK_INVENTORY_BALANCE_AvailableQuantity", "\"AvailableQuantity\" = \"PhysicalQuantity\" - \"HeldQuantity\" - \"ReservedQuantity\" AND \"AvailableQuantity\" >= 0");
        });
    }
}
