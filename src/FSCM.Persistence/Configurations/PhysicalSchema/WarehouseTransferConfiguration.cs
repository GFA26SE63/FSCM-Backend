using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class WarehouseTransferConfiguration : IEntityTypeConfiguration<WarehouseTransfer>
{
    public void Configure(EntityTypeBuilder<WarehouseTransfer> builder)
    {
        builder.ToTable("WAREHOUSE_TRANSFER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.TransferCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.SourceWarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.DestinationWarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.CreatedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ApprovedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.RejectionReason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.Property(entity => entity.RequestedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ApprovedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.DispatchedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ReceivedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.TransferCode).IsUnique();
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.SourceWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.DestinationWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
