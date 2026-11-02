using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FSCM.Persistence.Configurations;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class PickingListConfiguration : IEntityTypeConfiguration<PickingList>
{
    public void Configure(EntityTypeBuilder<PickingList> builder)
    {
        builder.ToTable("PICKING_LIST");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PickingCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.PurchaseOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.WarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.AssignedToUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.Status).HasConversion(new EnumCodeConverter("PickingStatus")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.StartedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ConfirmedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ConfirmedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.PickingCode).IsUnique();
        builder.HasIndex(entity => new { entity.PurchaseOrderId, entity.WarehouseId }).IsUnique();
        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("CK_PICKING_LIST_Status", "\"Status\" BETWEEN 0 AND 3"));
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
