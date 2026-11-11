using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class RetailerDebtPayOsCheckoutConfiguration : IEntityTypeConfiguration<RetailerDebtPayOsCheckout>
{
    public void Configure(EntityTypeBuilder<RetailerDebtPayOsCheckout> builder)
    {
        builder.ToTable("RETAILER_DEBT_PAYOS_CHECKOUT");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PaymentLinkId).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CheckoutUrl).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.QrCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.OperationKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.PayOsReference).HasMaxLength(74);
        builder.Property(x => x.WebhookHash).HasMaxLength(64);
        builder.HasIndex(x => x.OperationKey).IsUnique();
        builder.HasIndex(x => x.PayOsOrderCode).IsUnique();
        builder.HasOne<Retailer>().WithMany().HasForeignKey(x => x.RetailerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentReceipt>().WithMany().HasForeignKey(x => x.PaymentReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RetailerDebtPayOsAllocationConfiguration : IEntityTypeConfiguration<RetailerDebtPayOsAllocation>
{
    public void Configure(EntityTypeBuilder<RetailerDebtPayOsAllocation> builder)
    {
        builder.ToTable("RETAILER_DEBT_PAYOS_ALLOCATION");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.CheckoutId, x.PaymentId }).IsUnique();
        builder.HasOne<RetailerDebtPayOsCheckout>().WithMany().HasForeignKey(x => x.CheckoutId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
