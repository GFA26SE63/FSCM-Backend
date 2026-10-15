using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("VEHICLE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PlateNumber).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.VehicleType).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Capacity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.PlateNumber).IsUnique();
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
