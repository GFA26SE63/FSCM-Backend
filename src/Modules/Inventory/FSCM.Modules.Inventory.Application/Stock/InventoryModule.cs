using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Inventory.Domain.Batches;
using FSCM.Modules.Inventory.Domain.Warehouses;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Inventory.Application.Stock;

/// <summary>Implementation of the Inventory contract used by Ordering (allocation holds, approval reserves, picking issues).</summary>
public sealed class InventoryModule(IBatchRepository batches, IWarehouseRepository warehouses, IStockHoldRepository holds, ILabelRepository labels,
    IInventoryUnitOfWork uow, IClock clock, IdGenerator ids) : IInventoryModule
{
    public async Task<IReadOnlyList<AllocatableBatchDto>> AllocatableBatchesAsync(string sku, CancellationToken ct = default)
    {
        var whs = (await warehouses.AllAsync(ct)).ToDictionary(w => w.Id);
        return (await batches.BySkuAsync(sku, ct)).Where(b => b.Expiry > clock.Today && whs.ContainsKey(b.WarehouseCode))
            .Select(b => new AllocatableBatchDto(b.Id, b.WarehouseCode, whs[b.WarehouseCode].Name, whs[b.WarehouseCode].Type == WarehouseType.Central, b.Expiry, b.Available)).ToList();
    }

    public async Task HoldAsync(string orderCode, IReadOnlyList<HoldLine> lines, CancellationToken ct = default)
    {
        try
        {
            foreach (var l in lines)
            {
                var b = await batches.GetAsync(l.BatchCode, ct) ?? throw new BusinessRuleException("batch.not_found", l.BatchCode);
                b.Hold(l.Quantity);
                await holds.AddAsync(orderCode, b.Id, l.Quantity, ct);
            }
            await uow.SaveChangesAsync(ct); // version check: two Sales holding the same near-expiry batch at once → the second fails and Ordering retries on fresh stock
        }
        catch { uow.ClearTracked(); throw; }
    }

    public async Task ReleaseAsync(string orderCode, CancellationToken ct = default)
    {
        foreach (var h in await holds.ByOrderAsync(orderCode, ct))
        {
            var b = await batches.GetAsync(h.BatchCode, ct); if (b is null) continue;
            if (h.Reserved) b.Unreserve(h.Quantity); else b.ReleaseHold(h.Quantity);
        }
        await holds.RemoveAsync(orderCode, null, ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task ReserveAsync(string orderCode, CancellationToken ct = default)
    {
        var rows = await holds.ByOrderAsync(orderCode, ct);
        if (rows.Count == 0 || rows.All(r => r.Reserved)) return;                     // idempotent
        foreach (var h in rows.Where(r => !r.Reserved)) (await batches.GetAsync(h.BatchCode, ct))!.Reserve(h.Quantity);
        await holds.MarkReservedAsync(orderCode, ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<LabelInfoDto?> GetLabelAsync(string labelCode, CancellationToken ct = default)
    {
        var l = await labels.GetAsync(labelCode, ct); if (l is null) return null;
        var b = await batches.GetAsync(l.BatchCode, ct);
        return new LabelInfoDto(l.Id, l.BatchCode, b?.Sku ?? "", b?.WarehouseCode ?? "", l.Quantity, l.Status.ToString());
    }

    public async Task IssueAsync(string orderCode, string pickingCode, IReadOnlyList<IssueLine> lines, CancellationToken ct = default)
    {
        try
        {
            foreach (var l in lines)
            {
                var b = await batches.GetAsync(l.BatchCode, ct) ?? throw new BusinessRuleException("batch.not_found", l.BatchCode);
                b.Issue(l.Quantity);
                var label = await labels.GetAsync(l.LabelCode, ct) ?? throw new BusinessRuleException("label.not_found", $"Không có tem {l.LabelCode}.");
                Guard.Against(label.Status != LabelStatus.InStock || label.BatchCode != l.BatchCode, "label.state", $"Tem {l.LabelCode} không còn trong kho hoặc thuộc batch khác.");
                if (l.Quantity == label.Quantity) label.Ship(pickingCode);
                else labels.Add(label.Split(await ids.LabelAsync(ct), l.Quantity, pickingCode));
            }
            await holds.RemoveAsync(orderCode, lines.Select(l => l.BatchCode).Distinct().ToList(), ct);
            await uow.SaveChangesAsync(ct);
        }
        catch { uow.ClearTracked(); throw; }
    }
}

/// <summary>Nightly 01:30: mark batches near-expiry / expired (locks expired batches from allocation) and announce newly expired ones.</summary>
public sealed class DateTrackerJob(IBatchRepository batches, ICatalogModule catalog, IInventoryUnitOfWork uow, IClock clock) : IScheduledJob
{
    public string Name => "date-tracker";
    public TimeOnly RunAt => new(1, 30);
    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var b in await batches.AllActiveAsync(ct))
        {
            var before = b.State;
            b.Track(clock.Today, (await catalog.GetProductAsync(b.Sku, ct))?.NearExpiryDays ?? 0);
            if (before != ExpiryState.Expired && b.State == ExpiryState.Expired) uow.Enqueue(new BatchExpired(b.Id, b.WarehouseCode, b.Sku, b.OnHand));
        }
        await uow.SaveChangesAsync(ct);
    }
}
