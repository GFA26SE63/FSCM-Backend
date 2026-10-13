using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Pricing.Application;
using FSCM.Modules.Pricing.Domain.BatchDiscounts;
using FSCM.Modules.Pricing.Domain.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Pricing.Infrastructure;

public sealed class PricingDbContext(DbContextOptions<PricingDbContext> o) : ModuleDbContext(o), IPricingUnitOfWork
{
    protected override string Schema => "pricing";
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<BatchDiscount> BatchDiscounts => Set<BatchDiscount>();
}

internal sealed class PromotionConfig : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.ToTable("promotion"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(8);
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Type).HasConversion<string>().HasMaxLength(10); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.GroupPercent).HasPrecision(5, 2); b.Property(x => x.CategoryCode).HasMaxLength(8);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.OwnsMany(x => x.Rules, m => { m.ToTable("promotion_rule"); m.WithOwner().HasForeignKey("PromotionCode"); m.HasKey("PromotionCode", nameof(PromotionRule.Sku)); m.Property(x => x.Percent).HasPrecision(5, 2); });
        b.OwnsMany(x => x.GroupSkus, m => { m.ToTable("promotion_group_sku"); m.WithOwner().HasForeignKey("PromotionCode"); m.HasKey("PromotionCode", nameof(PromotionSku.Sku)); });
        b.OwnsMany(x => x.Retailers, m => { m.ToTable("promotion_retailer"); m.WithOwner().HasForeignKey("PromotionCode"); m.HasKey("PromotionCode", nameof(PromotionRetailer.RetailerCode)); });
        b.Navigation(x => x.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.GroupSkus).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Retailers).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.ValidFrom, x.ValidTo });
    }
}
internal sealed class BatchDiscountConfig : IEntityTypeConfiguration<BatchDiscount>
{
    public void Configure(EntityTypeBuilder<BatchDiscount> b)
    {
        b.ToTable("batch_discount"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(14);
        b.Property(x => x.BatchCode).HasMaxLength(32); b.Property(x => x.Percent).HasPrecision(5, 2); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.BatchCode);
    }
}
