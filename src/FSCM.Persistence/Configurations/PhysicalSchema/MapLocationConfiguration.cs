using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class MapLocationConfiguration : IEntityTypeConfiguration<MapLocation>
{
    public void Configure(EntityTypeBuilder<MapLocation> builder)
    {
        builder.ToTable("MAP_LOCATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.Latitude).HasPrecision(10, 7).IsRequired();
        builder.Property(entity => entity.Longitude).HasPrecision(10, 7).IsRequired();
        builder.Property(entity => entity.FormattedAddress).HasMaxLength(300).IsRequired(false);
        builder.Property(entity => entity.ProviderPlaceId).HasMaxLength(200).IsRequired(false);
        builder.Property(entity => entity.Provider).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Version).IsRequired();
        builder.Property(entity => entity.NormalizedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<Warehouse>()
            .WithOne()
            .HasForeignKey<MapLocation>(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Retailer>()
            .WithOne()
            .HasForeignKey<MapLocation>(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_MAP_LOCATION_Entity", "(\"WarehouseId\" IS NOT NULL) <> (\"RetailerId\" IS NOT NULL)"));
    }
}
