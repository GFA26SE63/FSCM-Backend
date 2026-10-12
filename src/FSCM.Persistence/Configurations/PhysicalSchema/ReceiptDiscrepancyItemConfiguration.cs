using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReceiptDiscrepancyItemConfiguration : IEntityTypeConfiguration<ReceiptDiscrepancyItem>
{
    public void Configure(EntityTypeBuilder<ReceiptDiscrepancyItem> builder)
    {
        builder.ToTable("RECEIPT_DISCREPANCY_ITEM");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReceiptDiscrepancyId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.BatchId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.IssueType).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ExpectedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.ActualReceivedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.EvidenceBlobUrl).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.HasOne<ReceiptDiscrepancy>()
            .WithMany()
            .HasForeignKey(entity => entity.ReceiptDiscrepancyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(entity => entity.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

