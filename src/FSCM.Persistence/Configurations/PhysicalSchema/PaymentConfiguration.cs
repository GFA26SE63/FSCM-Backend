using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FSCM.Persistence.Configurations;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("PAYMENT");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.Method).HasConversion(new EnumCodeConverter("PaymentMethod")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.Status).HasConversion(new EnumCodeConverter("PaymentStatus")).HasColumnType("smallint").IsRequired();
        builder.Property(entity => entity.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.AdjustedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.ConfirmedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.FinalApprovedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.FinalApprovalReference).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.DeliveredDate).HasColumnType("date").IsRequired(false);
        builder.Property(entity => entity.DueDate).HasColumnType("date").IsRequired(false);
        builder.Property(entity => entity.PaidDate).HasColumnType("date").IsRequired(false);
        builder.Property(entity => entity.CollectionMethod).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.OperationKey).HasMaxLength(80).IsRequired(false);
        builder.Property(entity => entity.RequestHash).HasMaxLength(64).IsRequired(false);
        builder.Property(entity => entity.PayOsOrderCode).IsRequired(false);
        builder.Property(entity => entity.PayOsPaymentLinkId).HasMaxLength(100).IsRequired(false);
        builder.Property(entity => entity.PayOsCheckoutUrl).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.PayOsQrCode).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.PayOsLinkStatus).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.PayOsExpiredAt).IsRequired(false);
        builder.Property(entity => entity.PayOsReference).HasMaxLength(100).IsRequired(false);
        builder.Property(entity => entity.PayOsWebhookHash).HasMaxLength(64).IsRequired(false);
        builder.Property(entity => entity.ConfirmedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.ConfirmedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.Version).IsRequired().IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.SalesOrderId).IsUnique();
        builder.HasIndex(entity => entity.OperationKey).IsUnique()
            .HasFilter("\"OperationKey\" IS NOT NULL");
        builder.HasIndex(entity => entity.PayOsOrderCode).IsUnique()
            .HasFilter("\"PayOsOrderCode\" IS NOT NULL");
        builder.HasIndex(entity => entity.PayOsPaymentLinkId).IsUnique()
            .HasFilter("\"PayOsPaymentLinkId\" IS NOT NULL");
        builder.HasIndex(entity => entity.PayOsReference).IsUnique()
            .HasFilter("\"PayOsReference\" IS NOT NULL");
        builder.HasIndex(entity => entity.PayOsWebhookHash).IsUnique()
            .HasFilter("\"PayOsWebhookHash\" IS NOT NULL");
        builder.HasIndex(entity => new { entity.Status, entity.DueDate });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_PAYMENT_Amount", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_PAYMENT_AdjustedAmount",
                "\"AdjustedAmount\" >= 0 AND \"AdjustedAmount\" <= \"Amount\"");
            table.HasCheckConstraint("CK_PAYMENT_ConfirmedAmount",
                "\"ConfirmedAmount\" >= 0 AND \"ConfirmedAmount\" <= \"Amount\"");
            table.HasCheckConstraint("CK_PAYMENT_Method", "\"Method\" BETWEEN 0 AND 1");
            table.HasCheckConstraint("CK_PAYMENT_Status", "\"Status\" BETWEEN 0 AND 3");
        });
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
