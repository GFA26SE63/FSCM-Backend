using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Logistics.Contracts;
using FSCM.Modules.Logistics.Domain.Fleet;
using FSCM.Modules.Logistics.Domain.Transit;
using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Logistics.Application;

public sealed class LogisticsModule(ITransitRuleRepository rules, IDistanceRepository distances, IFleetRepository fleet) : ILogisticsModule
{
    public async Task<TransitQuoteDto?> QuoteAsync(string fromWarehouse, string toPoint, Contracts.Purpose purpose, DateOnly onDate, CancellationToken ct = default)
    {
        var d = await distances.FindAsync(fromWarehouse, toPoint, ct);
        if (d is null) return null;
        var table = new TransitTable(await rules.InForceAsync(onDate, ct), await rules.LimitsAsync(ct));
        var q = table.Quote(d.Km, (RoutePurpose)(int)purpose);
        return q is null ? null : new TransitQuoteDto(q.Km, q.TransitDays, q.TripCost, q.Band);
    }

    public async Task<decimal?> DistanceKmAsync(string fromWarehouse, string toPoint, CancellationToken ct = default)
        => (await distances.FindAsync(fromWarehouse, toPoint, ct))?.Km;

    public async Task<DeliveryAssignmentDto> AssignDeliveryAsync(string plate, string driverCode, int cases, CancellationToken ct = default)
    {
        var v = await fleet.VehicleAsync(plate, ct) ?? throw new BusinessRuleException("vehicle.not_found", $"Không có xe {plate}.");
        var d = await fleet.DriverAsync(driverCode, ct) ?? throw new BusinessRuleException("driver.not_found", $"Không có người giao {driverCode}.");
        v.EnsureCanCarry(cases);
        Guard.Against(d.Status != FleetStatus.Active, "driver.inactive", $"{d.Id} đang tạm nghỉ.");
        return new DeliveryAssignmentDto(v.Id, v.Type, d.Id, d.Name, d.Phone);
    }

    /// <summary>One morning dispatch run at 07:00; 2 h picking/preparation + 1 h buffer before a run can leave.</summary>
    private static readonly EtaCalculator Eta = new(new[] { new TimeOnly(7, 0) }, TimeSpan.FromHours(2), TimeSpan.FromHours(1));

    public Task<DateOnly> NextDispatchDateAsync(DateTime localNow, CancellationToken ct = default) =>
        Task.FromResult(DateOnly.FromDateTime(Eta.NextDispatch(localNow)));

    public async Task<(decimal DmaxRetailerKm, decimal DmaxWarehouseKm)> LimitsAsync(CancellationToken ct = default)
    {
        var l = await rules.LimitsAsync(ct);
        return (l.DmaxRetailerKm, l.DmaxWarehouseKm);
    }
}

public sealed record TransitRuleInput(decimal FromKm, decimal ToKm, int TransitDays, decimal TripCost, bool ToRetailer, bool ToWarehouse);
public sealed record TransitTableDto(IReadOnlyList<TransitRuleInput> Rules, decimal DmaxRetailerKm, decimal DmaxWarehouseKm, DateOnly ValidFrom);

/// <summary>Admin screen "Cấu hình hệ thống → Bảng vận chuyển".</summary>
public sealed class TransitTableService(ITransitRuleRepository repo, ILogisticsUnitOfWork uow, IClock clock)
{
    public async Task<TransitTableDto> GetAsync(CancellationToken ct)
    {
        var rs = await repo.InForceAsync(clock.Today, ct); var l = await repo.LimitsAsync(ct);
        return new TransitTableDto(rs.Select(r => new TransitRuleInput(r.FromKm, r.ToKm, r.TransitDays, r.TripCost, r.AppliesTo.HasFlag(RoutePurpose.WarehouseToRetailer), r.AppliesTo.HasFlag(RoutePurpose.WarehouseToWarehouse))).ToList(),
            l.DmaxRetailerKm, l.DmaxWarehouseKm, rs.Count > 0 ? rs.Max(r => r.ValidFrom) : clock.Today);
    }

    /// <summary>Saves a new version effective from tomorrow so orders being processed today are not affected.</summary>
    public async Task UpdateAsync(TransitTableDto input, CancellationToken ct)
    {
        Guard.Against(input.DmaxRetailerKm > input.DmaxWarehouseKm, "transit.dmax", "Dmax_retailer không lớn hơn Dmax_kho.");
        var ordered = input.Rules.OrderBy(r => r.FromKm).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Guard.Against(ordered[i].FromKm != ordered[i - 1].ToKm, "transit.gap", "Các dải khoảng cách phải liền nhau, không chồng lấn.");
        var from = input.ValidFrom > clock.Today ? input.ValidFrom : clock.Today.AddDays(1);
        await repo.ReplaceTableAsync(ordered.Select(r => new TransitRule(r.FromKm, r.ToKm, r.TransitDays, r.TripCost,
            (r.ToRetailer ? RoutePurpose.WarehouseToRetailer : 0) | (r.ToWarehouse ? RoutePurpose.WarehouseToWarehouse : 0), from)), ct);
        await repo.SaveLimitsAsync(new DistanceLimits(input.DmaxRetailerKm, input.DmaxWarehouseKm), ct);
        await uow.SaveChangesAsync(ct);
    }
}

public sealed record VehicleDto(string Plate, string Type, int CapacityCases, string WarehouseCode, string Status);
public sealed record DriverDto(string Code, string Name, string Phone, string LicenseClass, string Status);

public sealed class FleetService(IFleetRepository repo, ILogisticsUnitOfWork uow)
{
    public async Task<IReadOnlyList<VehicleDto>> VehiclesAsync(bool activeOnly, CancellationToken ct) =>
        (await repo.VehiclesAsync(ct)).Where(v => !activeOnly || v.Status == FleetStatus.Active).Select(v => new VehicleDto(v.Id, v.Type, v.CapacityCases, v.WarehouseCode, v.Status.ToString())).ToList();
    public async Task<IReadOnlyList<DriverDto>> DriversAsync(bool activeOnly, CancellationToken ct) =>
        (await repo.DriversAsync(ct)).Where(d => !activeOnly || d.Status == FleetStatus.Active).Select(d => new DriverDto(d.Id, d.Name, d.Phone, d.LicenseClass, d.Status.ToString())).ToList();
    public async Task<string> AddVehicleAsync(VehicleDto x, CancellationToken ct)
    {
        Guard.Against(await repo.VehicleAsync(x.Plate, ct) is not null, "vehicle.exists", "Biển số đã tồn tại.");
        var v = new Vehicle(x.Plate, x.Type, x.CapacityCases, x.WarehouseCode); repo.Add(v); await uow.SaveChangesAsync(ct); return v.Id;
    }
    public async Task UpdateVehicleAsync(string plate, VehicleDto x, CancellationToken ct)
    {
        var v = await repo.VehicleAsync(plate, ct) ?? throw new BusinessRuleException("vehicle.not_found", $"Không có xe {plate}.");
        v.Update(x.Type, x.CapacityCases, x.WarehouseCode, Enum.Parse<FleetStatus>(x.Status, true)); await uow.SaveChangesAsync(ct);
    }
    public async Task UpdateDriverAsync(string code, DriverDto x, CancellationToken ct)
    {
        var d = await repo.DriverAsync(code, ct) ?? throw new BusinessRuleException("driver.not_found", $"Không có người giao {code}.");
        d.Update(x.Name, x.Phone, x.LicenseClass, Enum.Parse<FleetStatus>(x.Status, true)); await uow.SaveChangesAsync(ct);
    }
    public async Task<string> AddDriverAsync(DriverDto x, CancellationToken ct)
    {
        var code = SharedKernel.Ids.IdFormat.Driver(await repo.DriverCountAsync(ct) + 1);
        repo.Add(new Driver(code, x.Name, x.Phone, x.LicenseClass)); await uow.SaveChangesAsync(ct); return code;
    }
}
