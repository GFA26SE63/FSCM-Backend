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
        builder.Property(entity => entity.SalesOrderLineId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchQrLabelId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ExpectedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ReceivedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.DamagedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.MissingQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ScannedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.ReceiptConfirmationId, entity.SalesOrderLineId, entity.BatchId })
            .IsUnique().HasFilter("\"SalesOrderLineId\" IS NOT NULL");
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_RECEIPT_CONFIRMATION_ITEM_ACTUAL_QUANTITY",
            "\"DamagedQuantity\" >= 0 AND \"ReceivedQuantity\" >= \"DamagedQuantity\" " +
            "AND \"ExpectedQuantity\" >= \"ReceivedQuantity\" " +
            "AND \"MissingQuantity\" = \"ExpectedQuantity\" - \"ReceivedQuantity\""));
        builder.HasOne<ReceiptConfirmation>()
            .WithMany()
            .HasForeignKey(entity => entity.ReceiptConfirmationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrderLine>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BatchQrLabel>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchQrLabelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
