using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReceiptDiscrepancyConfiguration : IEntityTypeConfiguration<ReceiptDiscrepancy>
{
    public void Configure(EntityTypeBuilder<ReceiptDiscrepancy> builder)
    {
        builder.ToTable("RECEIPT_DISCREPANCY");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReceiptConfirmationId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReportedByRetailerUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReviewedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.ReportedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReviewedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ResolvedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReceiptConfirmation>()
            .WithMany()
            .HasForeignKey(entity => entity.ReceiptConfirmationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ReportedByRetailerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

