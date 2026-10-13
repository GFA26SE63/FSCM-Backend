using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Catalog.Application;
using FSCM.Modules.Catalog.Domain.Categories;
using FSCM.Modules.Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Catalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> o) : ModuleDbContext(o), ICatalogUnitOfWork
{
    protected override string Schema => "catalog";
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
}

internal sealed class CategoryConfig : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("category"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(8);
        b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.Version).IsConcurrencyToken();
    }
}
internal sealed class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("product"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("sku").HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.CategoryCode).HasMaxLength(8); b.Property(x => x.Pack).HasMaxLength(60);
        b.Property(x => x.BasePrice).HasPrecision(14, 0); b.Property(x => x.CostPrice).HasPrecision(14, 0); b.Property(x => x.DefaultMlorRatio).HasPrecision(4, 3);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        b.Ignore(x => x.Details);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.CategoryCode); b.HasIndex(x => x.Status);
    }
}
