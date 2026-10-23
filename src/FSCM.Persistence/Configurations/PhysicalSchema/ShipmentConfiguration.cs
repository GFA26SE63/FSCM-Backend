using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("SHIPMENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PickingListId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ShipmentCode).HasMaxLength(60).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.DriverId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.VehicleId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.DriverCodeSnapshot).HasMaxLength(20).IsRequired(false);
        builder.Property(entity => entity.DriverNameSnapshot).HasMaxLength(150).IsRequired(false);
        builder.Property(entity => entity.DriverPhoneSnapshot).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.DriverLicenseSnapshot).HasMaxLength(80).IsRequired(false);
        builder.Property(entity => entity.PlateNumberSnapshot).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.VehicleTypeSnapshot).HasMaxLength(50).IsRequired(false);
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.TotalQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ReceivedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ReadyAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.IssuedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.DispatchedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ReceivedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ExceptionReason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.ShipmentCode).IsUnique();
        builder.HasIndex(entity => new { entity.SalesOrderId, entity.WarehouseId }).IsUnique();
        builder.HasIndex(entity => entity.PickingListId).IsUnique();
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PickingList>().WithMany().HasForeignKey(entity => entity.PickingListId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Driver>().WithMany().HasForeignKey(entity => entity.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(entity => entity.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
