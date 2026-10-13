using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class QrLabelEventConfiguration : IEntityTypeConfiguration<QrLabelEvent>
{
    public void Configure(EntityTypeBuilder<QrLabelEvent> builder)
    {
        builder.ToTable("QR_LABEL_EVENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.OperationKey).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.EventType).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.FromStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ToStatus).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ReferenceCode).HasMaxLength(100).IsRequired(false);
        builder.Property(entity => entity.Note).HasMaxLength(500).IsRequired(false);
        builder.Property(entity => entity.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.OperationKey).IsUnique();
        builder.HasIndex(entity => new { entity.BatchQrLabelId, entity.OccurredAt });
        builder.HasOne<BatchQrLabel>().WithMany().HasForeignKey(entity => entity.BatchQrLabelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(entity => entity.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
