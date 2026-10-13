using System.Globalization;
using FSCM.Modules.Logistics.Application;
using FSCM.Modules.Logistics.Domain.Distances;
using FSCM.Modules.Logistics.Domain.Fleet;
using FSCM.Modules.Logistics.Domain.Transit;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Modules.Logistics.Infrastructure;

internal sealed class TransitRuleRepository(LogisticsDbContext db) : ITransitRuleRepository
{
    public async Task<IReadOnlyList<TransitRule>> InForceAsync(DateOnly onDate, CancellationToken ct)
    {
        var version = await db.TransitRules.Where(r => r.ValidFrom <= onDate).MaxAsync(r => (DateOnly?)r.ValidFrom, ct);
        return version is null ? Array.Empty<TransitRule>() : await db.TransitRules.Where(r => r.ValidFrom == version).ToListAsync(ct);
    }
    public async Task ReplaceTableAsync(IEnumerable<TransitRule> rules, CancellationToken ct)
    {
        var list = rules.ToList();
        var from = list[0].ValidFrom;
        db.TransitRules.RemoveRange(await db.TransitRules.Where(r => r.ValidFrom == from).ToListAsync(ct)); // same-day re-edit
        db.TransitRules.AddRange(list);
    }
    public async Task<DistanceLimits> LimitsAsync(CancellationToken ct)
    {
        var s = await db.Settings.Where(x => x.Key == "dmax_retailer_km" || x.Key == "dmax_warehouse_km").ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        decimal Get(string k, decimal d) => s.TryGetValue(k, out var v) ? decimal.Parse(v, CultureInfo.InvariantCulture) : d;
        return new DistanceLimits(Get("dmax_retailer_km", 20), Get("dmax_warehouse_km", 40));
    }
    public async Task SaveLimitsAsync(DistanceLimits l, CancellationToken ct)
    {
        foreach (var (k, v) in new[] { ("dmax_retailer_km", l.DmaxRetailerKm), ("dmax_warehouse_km", l.DmaxWarehouseKm) })
        {
            var row = await db.Settings.FindAsync(new object[] { k }, ct);
            if (row is null) db.Settings.Add(new LogisticsSetting { Key = k, Value = v.ToString(CultureInfo.InvariantCulture) });
            else row.Value = v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
internal sealed class DistanceRepository(LogisticsDbContext db) : IDistanceRepository
{
    public Task<DistanceEntry?> FindAsync(string fromWarehouse, string toPoint, CancellationToken ct) =>
        db.Distances.FirstOrDefaultAsync(x => (x.FromWarehouse == fromWarehouse && x.ToPoint == toPoint) || (x.FromWarehouse == toPoint && x.ToPoint == fromWarehouse), ct);
}
internal sealed class FleetRepository(LogisticsDbContext db) : IFleetRepository
{
    public Task<Vehicle?> VehicleAsync(string plate, CancellationToken ct) => db.Vehicles.FirstOrDefaultAsync(v => v.Id == plate, ct);
    public Task<Driver?> DriverAsync(string code, CancellationToken ct) => db.Drivers.FirstOrDefaultAsync(d => d.Id == code, ct);
    public async Task<IReadOnlyList<Vehicle>> VehiclesAsync(CancellationToken ct) => await db.Vehicles.OrderBy(v => v.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<Driver>> DriversAsync(CancellationToken ct) => await db.Drivers.OrderBy(v => v.Id).ToListAsync(ct);
    public void Add(Vehicle v) => db.Vehicles.Add(v);
    public void Add(Driver d) => db.Drivers.Add(d);
    public Task<int> DriverCountAsync(CancellationToken ct) => db.Drivers.CountAsync(ct);
}
