using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("WAREHOUSE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Code).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.RegionId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Address).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.MinimumStockThresholdCartons).IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.Code).IsUnique();
        builder.HasIndex(entity => entity.RegionId);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_WAREHOUSE_MinimumStockThresholdCartons", "\"MinimumStockThresholdCartons\" >= 0"));
        builder.HasOne<Region>()
            .WithMany()
            .HasForeignKey(entity => entity.RegionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
