using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Logistics.Domain.Distances;
using FSCM.Modules.Logistics.Domain.Fleet;
using FSCM.Modules.Logistics.Domain.Transit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Logistics.Infrastructure;

public sealed class LogisticsDbContext(DbContextOptions<LogisticsDbContext> o) : ModuleDbContext(o), FSCM.Modules.Logistics.Application.ILogisticsUnitOfWork
{
    protected override string Schema => "logistics";
    public DbSet<TransitRule> TransitRules => Set<TransitRule>();
    public DbSet<DistanceEntry> Distances => Set<DistanceEntry>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<LogisticsSetting> Settings => Set<LogisticsSetting>();
}

public sealed class LogisticsSetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }

internal sealed class TransitRuleConfig : IEntityTypeConfiguration<TransitRule>
{
    public void Configure(EntityTypeBuilder<TransitRule> b)
    {
        b.ToTable("transit_rule"); b.HasKey(x => x.Id);
        b.Property(x => x.FromKm).HasPrecision(6, 1); b.Property(x => x.ToKm).HasPrecision(6, 1); b.Property(x => x.TripCost).HasPrecision(14, 0);
        b.HasIndex(x => x.ValidFrom);
    }
}
internal sealed class DistanceConfig : IEntityTypeConfiguration<DistanceEntry>
{
    public void Configure(EntityTypeBuilder<DistanceEntry> b)
    {
        b.ToTable("distance"); b.HasKey(x => x.Id); b.Property(x => x.Km).HasPrecision(6, 1);
        b.HasIndex(x => new { x.FromWarehouse, x.ToPoint }).IsUnique();
    }
}
internal sealed class VehicleConfig : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b) { b.ToTable("vehicle"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("plate").HasMaxLength(12); b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.Status).HasConversion<string>(); }
}
internal sealed class DriverConfig : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> b) { b.ToTable("driver"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("code").HasMaxLength(6); b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.Status).HasConversion<string>(); }
}
internal sealed class SettingConfig : IEntityTypeConfiguration<LogisticsSetting>
{
    public void Configure(EntityTypeBuilder<LogisticsSetting> b) { b.ToTable("system_config"); b.HasKey(x => x.Key); }
}
