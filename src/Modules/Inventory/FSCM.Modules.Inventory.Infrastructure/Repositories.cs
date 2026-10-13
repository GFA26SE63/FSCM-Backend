using System.Globalization;
using FSCM.Modules.Inventory.Application;
using FSCM.Modules.Inventory.Application.Rotation;
using FSCM.Modules.Inventory.Domain.Batches;
using FSCM.Modules.Inventory.Domain.Disposals;
using FSCM.Modules.Inventory.Domain.Receipts;
using FSCM.Modules.Inventory.Domain.Rotation;
using FSCM.Modules.Inventory.Domain.Transfers;
using FSCM.Modules.Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Modules.Inventory.Infrastructure;

internal sealed class BatchRepository(InventoryDbContext db) : IBatchRepository
{
    public Task<Batch?> GetAsync(string code, CancellationToken ct) => db.Batches.FirstOrDefaultAsync(b => b.Id == code, ct);
    public async Task<IReadOnlyList<Batch>> BySkuAsync(string sku, CancellationToken ct) => await db.Batches.Where(b => b.Sku == sku && b.OnHand > 0).OrderBy(b => b.Expiry).ToListAsync(ct);
    public async Task<IReadOnlyList<Batch>> AllActiveAsync(CancellationToken ct) => await db.Batches.Where(b => b.OnHand > 0).ToListAsync(ct);
    public async Task<IReadOnlyList<Batch>> SearchAsync(string? warehouse, string? sku, ExpiryState? state, string? search, CancellationToken ct)
    {
        var q = db.Batches.AsQueryable();
        if (!string.IsNullOrWhiteSpace(warehouse)) q = q.Where(b => b.WarehouseCode == warehouse);
        if (!string.IsNullOrWhiteSpace(sku)) q = q.Where(b => b.Sku == sku);
        if (state is not null) q = q.Where(b => b.State == state);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(b => b.Id.Contains(search));
        return await q.ToListAsync(ct);
    }
    public void Add(Batch b) => db.Batches.Add(b);
}
internal sealed class WarehouseRepository(InventoryDbContext db) : IWarehouseRepository
{
    public async Task<IReadOnlyList<Warehouse>> AllAsync(CancellationToken ct) => await db.Warehouses.ToListAsync(ct);
    public Task<Warehouse?> GetAsync(string code, CancellationToken ct) => db.Warehouses.FirstOrDefaultAsync(w => w.Id == code, ct);
    public void Add(Warehouse w) => db.Warehouses.Add(w);
}
internal sealed class TransferRepository(InventoryDbContext db) : ITransferRepository
{
    public void Add(Transfer t) => db.Transfers.Add(t);
    public Task<Transfer?> GetAsync(string code, CancellationToken ct) => db.Transfers.FirstOrDefaultAsync(t => t.Id == code, ct);
    public Task<Transfer?> ByLabelAsync(string labelCode, CancellationToken ct) => db.Transfers.FirstOrDefaultAsync(t => t.LabelCode == labelCode, ct);
    public async Task<IReadOnlyList<Transfer>> RecentAsync(int take, CancellationToken ct) => await db.Transfers.OrderByDescending(t => t.CreatedAtUtc).Take(take).ToListAsync(ct);
}
internal sealed class RotationRepository(InventoryDbContext db) : IRotationRepository
{
    public async Task<IReadOnlyList<RotationProposal>> ByRunAsync(DateOnly d, CancellationToken ct) => await db.RotationProposals.Where(p => p.RunDate == d).OrderByDescending(p => p.Score).ToListAsync(ct);
    public Task<RotationProposal?> GetAsync(Guid id, CancellationToken ct) => db.RotationProposals.FirstOrDefaultAsync(p => p.Id == id, ct);
    public void AddRange(IEnumerable<RotationProposal> items) => db.RotationProposals.AddRange(items);
    public Task ExpireOpenAsync(DateOnly before, CancellationToken ct) =>
        db.RotationProposals.Where(p => p.RunDate < before && p.Status == ProposalStatus.Proposed).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, ProposalStatus.Expired), ct);
}
internal sealed class StockHoldRepository(InventoryDbContext db) : IStockHoldRepository
{
    public Task AddAsync(string orderCode, string batchCode, int qty, CancellationToken ct) { db.Holds.Add(new StockHold { OrderCode = orderCode, BatchCode = batchCode, Quantity = qty }); return Task.CompletedTask; }
    public async Task<IReadOnlyList<HoldRow>> ByOrderAsync(string orderCode, CancellationToken ct) =>
        (await db.Holds.Where(h => h.OrderCode == orderCode).ToListAsync(ct)).Select(h => new HoldRow(h.BatchCode, h.Quantity, h.Reserved)).ToList();
    public async Task MarkReservedAsync(string orderCode, CancellationToken ct) { foreach (var h in await db.Holds.Where(h => h.OrderCode == orderCode).ToListAsync(ct)) h.Reserved = true; }
    public async Task RemoveAsync(string orderCode, IReadOnlyCollection<string>? batchCodes, CancellationToken ct) =>
        db.Holds.RemoveRange(await db.Holds.Where(h => h.OrderCode == orderCode && (batchCodes == null || batchCodes.Contains(h.BatchCode))).ToListAsync(ct));
}
internal sealed class LabelRepository(InventoryDbContext db) : ILabelRepository
{
    public Task<Label?> GetAsync(string code, CancellationToken ct) => db.Labels.FirstOrDefaultAsync(l => l.Id == code, ct);
    public async Task<IReadOnlyList<Label>> ByBatchAsync(string batchCode, CancellationToken ct) => await db.Labels.Where(l => l.BatchCode == batchCode).ToListAsync(ct);
    public void Add(Label l) => db.Labels.Add(l);
}
internal sealed class GoodsReceiptRepository(InventoryDbContext db) : IGoodsReceiptRepository
{
    public Task<GoodsReceipt?> GetAsync(string code, CancellationToken ct) => db.GoodsReceipts.FirstOrDefaultAsync(r => r.Id == code, ct);
    public async Task<IReadOnlyList<GoodsReceipt>> SearchAsync(string? warehouse, ReceiptStatus? status, CancellationToken ct) =>
        await db.GoodsReceipts.Where(r => (warehouse == null || r.WarehouseCode == warehouse) && (status == null || r.Status == status)).OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
    public void Add(GoodsReceipt r) => db.GoodsReceipts.Add(r);
}
internal sealed class DisposalRepository(InventoryDbContext db) : IDisposalRepository
{
    public async Task<IReadOnlyList<Disposal>> RecentAsync(string? warehouse, int take, CancellationToken ct) =>
        await db.Disposals.Where(d => warehouse == null || d.WarehouseCode == warehouse).OrderByDescending(d => d.CreatedAtUtc).Take(take).ToListAsync(ct);
    public void Add(Disposal d) => db.Disposals.Add(d);
}
internal sealed class RotationSettingsProvider(InventoryDbContext db) : IRotationSettingsProvider
{
    public async Task<RotationSettings> GetAsync(CancellationToken ct)
    {
        var s = await db.Settings.ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        decimal D(string k, decimal d) => s.TryGetValue(k, out var v) ? decimal.Parse(v, CultureInfo.InvariantCulture) : d;
        int N(string k, int d) => s.TryGetValue(k, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : d;
        return new RotationSettings(D("rotation.w_e", .5m), D("rotation.w_d", .3m), D("rotation.w_q", .2m), D("dmax_warehouse_km", 40), N("rotation.min_qty", 10), N("rotation.round_to", 10), N("rotation.velocity_days", 14));
    }
    public async Task SaveAsync(RotationSettings v, CancellationToken ct)
    {
        var values = new Dictionary<string, string>
        {
            ["rotation.w_e"] = v.WeightSellThrough.ToString(CultureInfo.InvariantCulture), ["rotation.w_d"] = v.WeightDistance.ToString(CultureInfo.InvariantCulture),
            ["rotation.w_q"] = v.WeightCoverage.ToString(CultureInfo.InvariantCulture), ["dmax_warehouse_km"] = v.DmaxWarehouseKm.ToString(CultureInfo.InvariantCulture),
            ["rotation.min_qty"] = v.MinTransferQty.ToString(CultureInfo.InvariantCulture), ["rotation.round_to"] = v.RoundTo.ToString(CultureInfo.InvariantCulture),
            ["rotation.velocity_days"] = v.VelocityDays.ToString(CultureInfo.InvariantCulture)
        };
        var existing = await db.Settings.ToDictionaryAsync(x => x.Key, ct);
        foreach (var (k, val) in values)
            if (existing.TryGetValue(k, out var row)) row.Value = val; else db.Settings.Add(new InventorySetting { Key = k, Value = val });
        await db.SaveChangesAsync(ct);
    }
}
