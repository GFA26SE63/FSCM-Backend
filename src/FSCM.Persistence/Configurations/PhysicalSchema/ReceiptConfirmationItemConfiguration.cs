using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReceiptConfirmationItemConfiguration : IEntityTypeConfiguration<ReceiptConfirmationItem>
{
    public void Configure(EntityTypeBuilder<ReceiptConfirmationItem> builder)
    {
        builder.ToTable("RECEIPT_CONFIRMATION_ITEM");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReceiptConfirmationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchQrLabelId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ExpectedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ReceivedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ScannedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<ReceiptConfirmation>()
            .WithMany()
            .HasForeignKey(entity => entity.ReceiptConfirmationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BatchQrLabel>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchQrLabelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
