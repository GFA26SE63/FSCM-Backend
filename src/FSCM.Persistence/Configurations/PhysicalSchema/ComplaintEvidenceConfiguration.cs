using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ComplaintEvidenceConfiguration : IEntityTypeConfiguration<ComplaintEvidence>
{
    public void Configure(EntityTypeBuilder<ComplaintEvidence> builder)
    {
        builder.ToTable("COMPLAINT_EVIDENCE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReceiptDiscrepancyId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.UploadedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.FileName).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.BlobKey).HasColumnType("text").IsRequired();
        builder.Property(entity => entity.UploadedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.ReceiptDiscrepancyId);
        builder.HasOne<ReceiptDiscrepancy>()
            .WithMany()
            .HasForeignKey(entity => entity.ReceiptDiscrepancyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
