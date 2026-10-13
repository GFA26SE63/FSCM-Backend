using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Identity.Application;
using FSCM.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FSCM.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> o) : ModuleDbContext(o), IIdentityUnitOfWork
{
    protected override string Schema => "identity";
    public DbSet<AppUser> Users => Set<AppUser>();
}

internal sealed class AppUserConfig : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("app_user"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(40);
        b.Property(x => x.EntraObjectId).HasMaxLength(64); b.HasIndex(x => x.EntraObjectId).IsUnique();
        b.Property(x => x.FullName).HasMaxLength(150); b.Property(x => x.Email).HasMaxLength(200); b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Role); b.HasIndex(x => x.RetailerCode); b.HasIndex(x => x.WarehouseCode);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.OwnsMany(x => x.Devices, d =>
        {
            d.ToTable("user_device"); d.WithOwner().HasForeignKey("UserId");
            d.HasKey("UserId", nameof(UserDevice.DeviceId));
            d.Property(x => x.PushToken).HasMaxLength(500);
        });
        b.Navigation(x => x.Devices).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
