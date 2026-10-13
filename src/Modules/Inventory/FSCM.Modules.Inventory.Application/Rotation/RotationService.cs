using System.Text.Json;
using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Inventory.Domain.Rotation;
using FSCM.Modules.Inventory.Domain.Transfers;
using FSCM.Modules.Inventory.Domain.Warehouses;
using FSCM.Modules.Logistics.Contracts;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Partners.Contracts;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Inventory.Application.Rotation;

public interface IRotationSettingsProvider { Task<RotationSettings> GetAsync(CancellationToken ct); Task SaveAsync(RotationSettings s, CancellationToken ct); }

/// <summary>Nightly job 02:00 (after the date tracker). Builds snapshots, runs RotationPlanner, stores proposals.</summary>
public sealed class RotationJob(
    IBatchRepository batches, IWarehouseRepository warehouses, IRotationRepository proposals, IInventoryUnitOfWork uow,
    ICatalogModule catalog, IPartnersModule partners, IOrderingModule ordering, ILogisticsModule logistics,
    IRotationSettingsProvider settings, IClock clock, IEventBus bus) : IScheduledJob
{
    public string Name => "near-expiry-rotation";
    public TimeOnly RunAt => new(2, 0);

    public async Task RunAsync(CancellationToken ct)
    {
        var today = clock.Today; var s = await settings.GetAsync(ct);
        var whs = (await warehouses.AllAsync(ct)).Where(w => w.Active).ToDictionary(w => w.Id, w => new WarehouseInfo(w.Id, w.Name, w.Type == WarehouseType.Central));
        var products = new Dictionary<string, ProductDto>();
        var snaps = new List<BatchSnapshot>();
        foreach (var b in await batches.AllActiveAsync(ct))
        {
            if (!products.TryGetValue(b.Sku, out var pr)) products[b.Sku] = pr = (await catalog.GetProductAsync(b.Sku, ct))!;
            snaps.Add(new BatchSnapshot(b.Id, b.WarehouseCode, b.Sku, b.Expiry, b.Available, pr.ShelfLifeDays, pr.CostPrice));
        }

        // sales velocity per retailer over the last N days, from issued orders (Ordering module owns orders)
        var from = today.AddDays(-s.VelocityDays);
        var vel = new Dictionary<(string, string), IReadOnlyList<CustomerVelocity>>();
        foreach (var key in snaps.Select(x => x.Sku).Distinct().SelectMany(sku => whs.Keys.Select(w => (w, sku))))
        {
            var rows = await ordering.OutboundByRetailerAsync(key.w, key.sku, from, today, ct);
            var hasHistory = rows.Count > 0 && rows.Min(r => r.FirstIssueDate) <= from;
            var list = new List<CustomerVelocity>();
            if (hasHistory)
                foreach (var r in rows)
                    list.Add(new CustomerVelocity(r.RetailerCode, (decimal)r.Cases / s.VelocityDays, await partners.MlorRatioAsync(r.RetailerCode, key.sku, ct) ?? products[key.sku].DefaultMlorRatio));
            vel[key] = list; // < VelocityDays of history → empty → no proposal for this warehouse/SKU
        }

        var legs = new Dictionary<(string, string), Leg?>();
        foreach (var a in whs.Keys) foreach (var b in whs.Keys.Where(x => x != a))
        {
            var q = await logistics.QuoteAsync(a, b, Purpose.WarehouseToWarehouse, today, ct);
            legs[(a, b)] = q is null ? null : new Leg(q.Km, q.TransitDays, q.TripCost);
        }

        var plans = new RotationPlanner(s).Plan(today, snaps, whs, (w, sku) => vel.GetValueOrDefault((w, sku)) ?? Array.Empty<CustomerVelocity>(), (a, b) => legs.GetValueOrDefault((a, b)));
        await proposals.ExpireOpenAsync(today, ct);
        var items = plans.SelectMany(pl => pl.Assignments.Select(a =>
            new RotationProposal(today, pl.Batch.Code, pl.Batch.Warehouse, a.ToWarehouse, pl.Excess, pl.Candidates.First(c => c.Warehouse == a.ToWarehouse).ReceivingCapacity,
                a.Quantity, a.Score, a.SalvageValue, a.TripCost, JsonSerializer.Serialize(pl)))).ToList();
        proposals.AddRange(items);
        await uow.SaveChangesAsync(ct);
        await bus.PublishAsync(new RotationProposalsCreated(today, items.Count), ct);
    }
}

public sealed record ProposalDto(Guid Id, string BatchCode, string From, string To, int Excess, int Quantity, decimal Score, decimal SalvageValue, decimal TripCost, string Status, string? TransferCode);

public sealed class RotationService(IRotationRepository proposals, IBatchRepository batches, ITransferRepository transfers, IInventoryUnitOfWork uow,
    IdGenerator ids, ICurrentUser user, IClock clock, IRotationSettingsProvider settings, IEventBus bus)
{
    /// <summary>Proposals of one nightly run (default: today) – Portal "Luân chuyển cận hạn".</summary>
    public async Task<IReadOnlyList<ProposalDto>> ByRunDateAsync(DateOnly? runDate, CancellationToken ct) =>
        (await proposals.ByRunAsync(runDate ?? clock.Today, ct)).Select(Map).ToList();

    public Task<RotationSettings> GetSettingsAsync(CancellationToken ct) => settings.GetAsync(ct);
    public async Task SaveSettingsAsync(RotationSettings s, CancellationToken ct)
    {
        Guard.Against(Math.Abs(s.WeightSellThrough + s.WeightDistance + s.WeightCoverage - 1m) > 0.001m, "rotation.weights", "Tổng ba trọng số phải bằng 1.");
        await settings.SaveAsync(s, ct);
    }

    public async Task<string> TraceAsync(Guid id, CancellationToken ct) => (await Get(id, ct)).TraceJson;

    public async Task ChangeQuantityAsync(Guid id, int qty, CancellationToken ct)
    {
        var p = await Get(id, ct); p.ChangeQuantity(qty, (await settings.GetAsync(ct)).MinTransferQty); await uow.SaveChangesAsync(ct);
    }

    public async Task SkipAsync(Guid id, CancellationToken ct) { (await Get(id, ct)).Skip(user.FullName); await uow.SaveChangesAsync(ct); }

    /// <summary>Approve → transfer DC of kind NearExpiryRotation; stock leaves the source batch now, enters the destination batch when scanned in.</summary>
    public async Task<string> ApproveAsync(Guid id, CancellationToken ct)
    {
        var p = await Get(id, ct);
        var b = await batches.GetAsync(p.BatchCode, ct) ?? throw new BusinessRuleException("batch.not_found", p.BatchCode);
        b.TransferOut(p.Quantity);
        var code = await ids.DocumentAsync(DocumentType.Transfer, ct);
        var t = new Transfer(code, TransferKind.NearExpiryRotation, p.FromWarehouse, p.ToWarehouse, b.Sku, b.Id, b.Expiry, p.Quantity, await ids.LabelAsync(ct), user.FullName, p.Score);
        transfers.Add(t);
        p.Approve(user.FullName, code);
        await uow.SaveChangesAsync(ct);
        await bus.PublishAsync(new TransferCreated(code, "NearExpiryRotation", t.FromWarehouse, t.ToWarehouse, t.Sku, t.Quantity), ct);
        return code;
    }

    private async Task<RotationProposal> Get(Guid id, CancellationToken ct) => await proposals.GetAsync(id, ct) ?? throw new BusinessRuleException("proposal.not_found", "Không có đề xuất.");
    private static ProposalDto Map(RotationProposal p) => new(p.Id, p.BatchCode, p.FromWarehouse, p.ToWarehouse, p.Excess, p.Quantity, p.Score, p.SalvageValue, p.TripCost, p.Status.ToString(), p.TransferCode);
}
