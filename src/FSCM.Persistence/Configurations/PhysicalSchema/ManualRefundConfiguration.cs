using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ManualRefundConfiguration : IEntityTypeConfiguration<ManualRefund>
{
    public void Configure(EntityTypeBuilder<ManualRefund> builder)
    {
        builder.ToTable("MANUAL_REFUND");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("uuid").IsRequired();
        builder.Property(x => x.PaymentId).HasColumnType("uuid").IsRequired();
        builder.Property(x => x.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Recipient).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.CreateOperationKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.CreateRequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EvidenceOperationKey).HasMaxLength(80);
        builder.Property(x => x.EvidenceRequestHash).HasMaxLength(64);
        builder.Property(x => x.TransferredAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.TransactionReference).HasMaxLength(100);
        builder.Property(x => x.EvidenceReference).HasMaxLength(500);
        builder.Property(x => x.SubmittedByUserId).HasColumnType("uuid");
        builder.Property(x => x.SubmittedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.VerificationOperationKey).HasMaxLength(80);
        builder.Property(x => x.VerificationRequestHash).HasMaxLength(64);
        builder.Property(x => x.VerifiedByUserId).HasColumnType("uuid");
        builder.Property(x => x.VerifiedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.VerificationNote).HasMaxLength(500);
        builder.Property(x => x.CreatedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(x => x.CreateOperationKey).IsUnique();
        builder.HasIndex(x => x.EvidenceOperationKey).IsUnique().HasFilter("\"EvidenceOperationKey\" IS NOT NULL");
        builder.HasIndex(x => x.VerificationOperationKey).IsUnique().HasFilter("\"VerificationOperationKey\" IS NOT NULL");
        builder.HasIndex(x => new { x.SalesOrderId, x.Status });
        builder.ToTable(table => table.HasCheckConstraint("CK_MANUAL_REFUND_Amount", "\"Amount\" > 0"));
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.VerifiedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
