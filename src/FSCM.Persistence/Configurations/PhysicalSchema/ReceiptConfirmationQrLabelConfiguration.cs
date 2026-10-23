using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReceiptConfirmationQrLabelConfiguration : IEntityTypeConfiguration<ReceiptConfirmationQrLabel>
{
    public void Configure(EntityTypeBuilder<ReceiptConfirmationQrLabel> builder)
    {
        builder.ToTable("RECEIPT_CONFIRMATION_QR_LABEL");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReceiptConfirmationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchQrLabelId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ScannedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.ReceiptConfirmationId, entity.BatchQrLabelId }).IsUnique();
        builder.HasIndex(entity => entity.BatchQrLabelId).IsUnique();
        builder.HasOne<ReceiptConfirmation>().WithMany()
            .HasForeignKey(entity => entity.ReceiptConfirmationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BatchQrLabel>().WithMany()
            .HasForeignKey(entity => entity.BatchQrLabelId).OnDelete(DeleteBehavior.Restrict);
    }
}
