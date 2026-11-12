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
        builder.Property(x => x.RequestType).HasMaxLength(30).HasDefaultValue("Complaint");
        builder.Property(x => x.SettlementMethod).HasMaxLength(20).HasDefaultValue("DebtOffset");
        builder.Property(x => x.ApprovedSettlementMethod).HasMaxLength(20);
        builder.Property(x => x.RequestedQuantity).HasPrecision(18,3);
        builder.Property(x => x.ApprovedQuantity).HasPrecision(18,3);
        builder.Property(x => x.HandledQuantity).HasPrecision(18,3);
        builder.Property(x => x.DebtOffsetAmount).HasPrecision(18,0);
        builder.Property(x => x.RefundAmount).HasPrecision(18,0);
        builder.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(x => x.RequestHash).HasMaxLength(64);
        builder.HasIndex(x => new { x.ReportedByRetailerUserId, x.RequestId }).IsUnique().HasFilter("\"RequestId\" IS NOT NULL");
        builder.ToTable(t => {
            t.HasCheckConstraint("CK_REQUEST_Type", "\"RequestType\" IN ('Complaint','Return','DamagedCollection')");
            t.HasCheckConstraint("CK_REQUEST_Settlement", "\"SettlementMethod\" IN ('DebtOffset','CashRefund','None') AND (\"ApprovedSettlementMethod\" IS NULL OR \"ApprovedSettlementMethod\" IN ('DebtOffset','CashRefund','None'))");
            t.HasCheckConstraint("CK_REQUEST_Quantities", "\"RequestedQuantity\" >= 0 AND (\"ApprovedQuantity\" IS NULL OR (\"ApprovedQuantity\" > 0 AND \"ApprovedQuantity\" <= \"RequestedQuantity\")) AND (\"HandledQuantity\" IS NULL OR (\"HandledQuantity\" >= 0 AND \"HandledQuantity\" <= \"ApprovedQuantity\"))");
        });
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_RECEIPT_DISCREPANCY_Status", "\"Status\" BETWEEN 0 AND 5");
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
