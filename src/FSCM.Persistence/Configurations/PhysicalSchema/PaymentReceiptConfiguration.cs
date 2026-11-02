using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class PaymentReceiptConfiguration : IEntityTypeConfiguration<PaymentReceipt>
{
    public void Configure(EntityTypeBuilder<PaymentReceipt> builder)
    {
        builder.ToTable("PAYMENT_RECEIPT", table =>
        {
            table.HasCheckConstraint("CK_PAYMENT_RECEIPT_ReceivedAmount", "\"ReceivedAmount\" > 0");
            table.HasCheckConstraint("CK_PAYMENT_RECEIPT_UnappliedCreditAmount", "\"UnappliedCreditAmount\" >= 0 AND \"UnappliedCreditAmount\" <= \"ReceivedAmount\"");
            table.HasCheckConstraint("CK_PAYMENT_RECEIPT_Status", "\"Status\" IN (0, 1, 2)");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid");
        builder.Property(entity => entity.ReceiptCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReceivedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.UnappliedCreditAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.Status).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.Reference).HasMaxLength(100);
        builder.Property(entity => entity.Note).HasColumnType("text");
        builder.Property(entity => entity.ConfirmedByUserId).HasColumnType("uuid");
        builder.Property(entity => entity.ConfirmedAt).HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ReversedByUserId).HasColumnType("uuid");
        builder.Property(entity => entity.ReversedAt).HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ReversalReason).HasColumnType("text");
        builder.Property(entity => entity.OperationKey).HasMaxLength(80).IsRequired();
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.ReceiptCode).IsUnique();
        builder.HasIndex(entity => entity.OperationKey).IsUnique();
        builder.HasIndex(entity => new { entity.RetailerId, entity.Status });
        builder.HasOne<Retailer>().WithMany().HasForeignKey(entity => entity.RetailerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.ConfirmedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => entity.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentReceiptAllocationConfiguration : IEntityTypeConfiguration<PaymentReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentReceiptAllocation> builder)
    {
        builder.ToTable("PAYMENT_RECEIPT_ALLOCATION", table =>
            table.HasCheckConstraint("CK_PAYMENT_RECEIPT_ALLOCATION_Amount", "\"Amount\" > 0"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid");
        builder.Property(entity => entity.PaymentReceiptId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.PaymentId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => new { entity.PaymentReceiptId, entity.PaymentId }).IsUnique();
        builder.HasIndex(entity => entity.PaymentId);
        builder.HasOne<PaymentReceipt>().WithMany().HasForeignKey(entity => entity.PaymentReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(entity => entity.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
