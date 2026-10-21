using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("SALES_ORDER_LINE");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LineNumber).IsRequired();
        builder.Property(entity => entity.SkuSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.ProductNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.OrderedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.GrossAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.DiscountAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.DiscountPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(entity => entity.DiscountSourceType).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.DiscountSourceCode).HasMaxLength(40).IsRequired(false);
        builder.Property(entity => entity.NetAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.EstimatedArrivalAt).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(entity => entity.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.SalesOrderId, entity.LineNumber }).IsUnique();
    }
}
