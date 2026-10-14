using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class BatchQrLabelConfiguration : IEntityTypeConfiguration<BatchQrLabel>
{
    public void Configure(EntityTypeBuilder<BatchQrLabel> builder)
    {
        builder.ToTable("BATCH_QR_LABEL");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.CurrentWarehouseId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ParentLabelId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.RootLabelId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.QrCode).HasMaxLength(120).IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.BlobUrl).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.Property(entity => entity.GeneratedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.InvalidatedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => entity.QrCode).IsUnique();
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entity => entity.CurrentWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BatchQrLabel>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentLabelId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BatchQrLabel>()
            .WithMany()
            .HasForeignKey(entity => entity.RootLabelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
