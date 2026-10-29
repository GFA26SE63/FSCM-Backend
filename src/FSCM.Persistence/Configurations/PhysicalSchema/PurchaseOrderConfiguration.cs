using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PURCHASE_ORDER");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PurchaseOrderCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => x.PurchaseOrderCode).IsUnique();
        builder.HasIndex(x => x.SalesOrderQuoteId).IsUnique();
    }
}
