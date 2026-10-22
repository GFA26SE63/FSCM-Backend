using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Persistence.Configurations.PhysicalSchema;

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("SALES_ORDER_LINE", table =>
            table.HasCheckConstraint("CK_SALES_ORDER_LINE_MLOR_RATIO",
                "\"MlorMinimumLifeRatio\" > 0 AND \"MlorMinimumLifeRatio\" < 1"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.SalesOrderId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.LineNumber).IsRequired();
        builder.Property(entity => entity.SkuSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.ProductNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.OrderedQuantity).HasPrecision(18, 3).IsRequired();
        builder.Property(entity => entity.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.ProductPriceId).HasColumnType("uuid").IsRequired();
        builder.Property(entity => entity.ProductPriceEffectiveFrom).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ProductPriceEffectiveTo).HasColumnType("timestamp with time zone").IsRequired(false);
        builder.Property(entity => entity.PricedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.GrossAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.DiscountAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.DiscountPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(entity => entity.DiscountSourceType).HasMaxLength(30).IsRequired(false);
        builder.Property(entity => entity.DiscountSourceCode).HasMaxLength(40).IsRequired(false);
        builder.Property(entity => entity.NetAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(entity => entity.FinalPricePendingAllocation).IsRequired();
        builder.Property(entity => entity.MlorSourceScope).HasMaxLength(20).IsRequired();
        builder.Property(entity => entity.MlorSourceRuleId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.MlorSourceVersion).IsRequired();
        builder.Property(entity => entity.MlorProductCategoryId).HasColumnType("uuid").IsRequired(false);
        builder.Property(entity => entity.MlorMinimumLifeRatio).HasPrecision(6, 5).IsRequired();
        builder.Property(entity => entity.MlorMinimumLifeHours).IsRequired(false);
        builder.Property(entity => entity.MlorResolvedAt).HasColumnType("timestamp with time zone").IsRequired();
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
        builder.HasOne<ProductPrice>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductPriceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RetailerProductMlor>()
            .WithMany()
            .HasForeignKey(entity => entity.MlorSourceRuleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(entity => entity.MlorProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.SalesOrderId, entity.LineNumber }).IsUnique();
    }
}
