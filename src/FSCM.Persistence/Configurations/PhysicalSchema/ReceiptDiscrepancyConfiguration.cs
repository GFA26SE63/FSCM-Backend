using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FSCM.Persistence.Configurations;

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
        builder.Property(entity => entity.Status).HasConversion(new EnumCodeConverter("ClaimStatus")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.ResolutionType).HasConversion(new NullableEnumCodeConverter("ClaimResolutionType")).HasColumnType("smallint").IsRequired(false);
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.ReportedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReviewedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.ResolvedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_RECEIPT_DISCREPANCY_Status", "\"Status\" BETWEEN 0 AND 4");
            table.HasCheckConstraint("CK_RECEIPT_DISCREPANCY_ResolutionType", "\"ResolutionType\" IS NULL OR \"ResolutionType\" BETWEEN 0 AND 1");
        });
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
