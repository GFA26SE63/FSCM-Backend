using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("SALES_ORDER");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OrderCode).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.RetailerId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RetailerCodeSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.RetailerNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.SalesGroupId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.CreatedByUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ReviewedByUserId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.ClientOrderKey).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Source).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.PaymentMethod).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.DeliveryAddress).HasColumnType("text").IsRequired();
        builder.Property(entity => entity.RequestedDeliveryDate).HasColumnType("date").IsRequired(false);
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreditTermDays).IsRequired();
        builder.Property(entity => entity.SubtotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.DiscountAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.FinalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.OfflineEstimatedAmount).HasPrecision(18, 2).IsRequired(false);
        builder.Property(entity => entity.HasOfflineDifference).IsRequired();
        builder.Property(entity => entity.ServerReceivedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ReviewedAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.RejectionReason).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.AllocationTraceJson).HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.Version).IsRequired().IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.OrderCode).IsUnique();
        builder.HasIndex(entity => entity.ClientOrderKey).IsUnique();
        builder.HasIndex(entity => new { entity.CreatedByUserId, entity.ServerReceivedAt });
        builder.HasOne<Retailer>()
            .WithMany()
            .HasForeignKey(entity => entity.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesGroup>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesGroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
