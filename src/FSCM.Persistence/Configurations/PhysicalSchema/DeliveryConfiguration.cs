using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("DELIVERY");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.DriverId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.VehicleId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OperationKey).HasMaxLength(80).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.DriverCodeSnapshot).HasMaxLength(20).IsRequired();
        builder.Property(entity => entity.DriverNameSnapshot).HasMaxLength(150).IsRequired();
        builder.Property(entity => entity.DriverPhoneSnapshot).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.DriverLicenseSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(entity => entity.PlateNumberSnapshot).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.VehicleTypeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.Version).IsRequired().IsConcurrencyToken();
        builder.Property(entity => entity.AssignedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.DispatchedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.DeliveredAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.SalesOrderId).IsUnique();
        builder.HasIndex(entity => entity.OperationKey).IsUnique();
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Driver>()
            .WithMany()
            .HasForeignKey(entity => entity.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(entity => entity.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
