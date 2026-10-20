using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class MapDistanceCacheConfiguration : IEntityTypeConfiguration<MapDistanceCache>
{
    public void Configure(EntityTypeBuilder<MapDistanceCache> builder)
    {
        builder.ToTable("MAP_DISTANCE_CACHE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OriginLocationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.DestinationLocationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Provider).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.ProviderStatus).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.CalculatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.WarehouseId, entity.RetailerId, entity.Version }).IsUnique();
        builder.HasIndex(entity => new { entity.WarehouseId, entity.RetailerId })
            .IsUnique().HasFilter("\"IsActive\" = TRUE");
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Retailer>().WithMany().HasForeignKey(entity => entity.RetailerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MapLocation>().WithMany().HasForeignKey(entity => entity.OriginLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MapLocation>().WithMany().HasForeignKey(entity => entity.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_MAP_DISTANCE_CACHE_Distance", "\"DistanceMeters\" > 0");
            table.HasCheckConstraint("CK_MAP_DISTANCE_CACHE_Duration", "\"DurationSeconds\" > 0");
        });
    }
}
