using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReturnHandlingDocumentConfiguration : IEntityTypeConfiguration<ReturnHandlingDocument>
{
    public void Configure(EntityTypeBuilder<ReturnHandlingDocument> b)
    {
        b.ToTable("RETURN_HANDLING_DOCUMENT", t => {
            t.HasCheckConstraint("CK_RETURN_HANDLING_Kind", "\"Kind\" IN ('ReturnOrder','CollectionOrder')");
            t.HasCheckConstraint("CK_RETURN_HANDLING_Status", "\"Status\" IN ('Pending','Completed','Waived')");
            t.HasCheckConstraint("CK_RETURN_HANDLING_Quantity", "\"ApprovedQuantity\" > 0 AND (\"ActualQuantity\" IS NULL OR (\"ActualQuantity\" >= 0 AND \"ActualQuantity\" <= \"ApprovedQuantity\"))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ApprovedQuantity).HasPrecision(18,3);
        b.Property(x => x.ActualQuantity).HasPrecision(18,3);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.EvidenceReference).HasMaxLength(2000);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => new { x.ReceiptDiscrepancyId, x.Kind }).IsUnique();
        b.HasOne<ReceiptDiscrepancy>().WithMany().HasForeignKey(x => x.ReceiptDiscrepancyId).OnDelete(DeleteBehavior.Restrict);
    }
}
