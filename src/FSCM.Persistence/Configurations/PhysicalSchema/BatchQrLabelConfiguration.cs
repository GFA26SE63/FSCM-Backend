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
        builder.Property(entity => entity.QrCode).HasMaxLength(120).IsRequired();
        builder.Property(entity => entity.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.BlobUrl).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.GeneratedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.InvalidatedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasIndex(entity => entity.QrCode).IsUnique();
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
