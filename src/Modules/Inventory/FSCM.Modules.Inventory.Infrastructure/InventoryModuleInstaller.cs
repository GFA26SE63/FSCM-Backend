using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using FSCM.Modules.Inventory.Application;
using FSCM.Modules.Inventory.Application.Rotation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Inventory.Infrastructure;

public sealed class InventoryModuleInstaller : IModuleInstaller
{
    public string Name => "Inventory";
    public void Register(IServiceCollection s, IConfiguration cfg)
    {
        s.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Fscm"), x => x.MigrationsHistoryTable("__migrations", "inventory")));
        s.AddScoped<ModuleDbContext>(sp => sp.GetRequiredService<InventoryDbContext>());
        s.AddScoped<IInventoryUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());
        s.AddScoped<IBatchRepository, BatchRepository>();
        s.AddScoped<IWarehouseRepository, WarehouseRepository>();
        s.AddScoped<ITransferRepository, TransferRepository>();
        s.AddScoped<IRotationRepository, RotationRepository>();
        s.AddScoped<IStockHoldRepository, StockHoldRepository>();
        s.AddScoped<ILabelRepository, LabelRepository>();
        s.AddScoped<IGoodsReceiptRepository, GoodsReceiptRepository>();
        s.AddScoped<IDisposalRepository, DisposalRepository>();
        s.AddScoped<IRotationSettingsProvider, RotationSettingsProvider>();
        s.AddInventoryApplication();
    }
}
