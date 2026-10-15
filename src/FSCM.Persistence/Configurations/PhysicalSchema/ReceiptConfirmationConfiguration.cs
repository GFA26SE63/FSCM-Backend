using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class ReceiptConfirmationConfiguration : IEntityTypeConfiguration<ReceiptConfirmation>
{
    public void Configure(EntityTypeBuilder<ReceiptConfirmation> builder)
    {
        builder.ToTable("RECEIPT_CONFIRMATION");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ConfirmedByRetailerUserId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.OperationKey).HasMaxLength(80).IsRequired();
        builder.Property(entity => entity.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Status).HasMaxLength(30).IsRequired();
        builder.Property(entity => entity.ConfirmedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.Note).HasColumnType("text").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(entity => entity.OperationKey).IsUnique();
        builder.HasOne<SalesOrder>()
            .WithOne()
            .HasForeignKey<ReceiptConfirmation>(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => entity.ConfirmedByRetailerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
