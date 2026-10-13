using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Partners.Application;
using FSCM.Modules.Partners.Domain.Areas;
using FSCM.Modules.Partners.Domain.Retailers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Partners.Infrastructure;

public sealed class PartnersDbContext(DbContextOptions<PartnersDbContext> o) : ModuleDbContext(o), IPartnersUnitOfWork
{
    protected override string Schema => "partners";
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<SalesGroup> SalesGroups => Set<SalesGroup>();
    public DbSet<Retailer> Retailers => Set<Retailer>();
    public DbSet<RetailerRegistration> Registrations => Set<RetailerRegistration>();
}

internal sealed class AreaConfig : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> b) { b.ToTable("area"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(4); b.Property(x => x.Version).IsConcurrencyToken(); }
}
internal sealed class SalesGroupConfig : IEntityTypeConfiguration<SalesGroup>
{
    public void Configure(EntityTypeBuilder<SalesGroup> b)
    {
        b.ToTable("sales_group"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(6);
        b.HasIndex(x => x.AreaCode); b.Property(x => x.Version).IsConcurrencyToken();
        b.OwnsMany(x => x.Members, m =>
        {
            m.ToTable("sales_group_member"); m.WithOwner().HasForeignKey("GroupCode");
            m.HasKey("GroupCode", nameof(SalesMember.UserId));
            m.HasIndex(x => x.UserId).IsUnique(); // a Sales belongs to one group only
        });
        b.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class RetailerConfig : IEntityTypeConfiguration<Retailer>
{
    public void Configure(EntityTypeBuilder<Retailer> b)
    {
        b.ToTable("retailer"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(8);
        b.Property(x => x.Name).HasMaxLength(200); b.HasIndex(x => x.AreaCode); b.HasIndex(x => x.UpdatedAtUtc);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.OwnsOne(x => x.Potential, p => { p.Property(x => x.ForecastMonthlyRevenue).HasPrecision(14, 0); p.Property(x => x.StoreSizeM2).HasPrecision(8, 1); });
        b.OwnsMany(x => x.Mlor, m =>
        {
            m.ToTable("retailer_sku_mlor"); m.WithOwner().HasForeignKey("RetailerCode");
            m.HasKey("RetailerCode", nameof(RetailerSkuMlor.Sku)); m.Property(x => x.Ratio).HasPrecision(4, 3);
        });
        b.Navigation(x => x.Mlor).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class RegistrationConfig : IEntityTypeConfiguration<RetailerRegistration>
{
    public void Configure(EntityTypeBuilder<RetailerRegistration> b)
    {
        b.ToTable("retailer_registration"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(8);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); b.HasIndex(x => x.Status);
        b.OwnsOne(x => x.Potential, p => { p.Property(x => x.ForecastMonthlyRevenue).HasPrecision(14, 0); p.Property(x => x.StoreSizeM2).HasPrecision(8, 1); });
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}
