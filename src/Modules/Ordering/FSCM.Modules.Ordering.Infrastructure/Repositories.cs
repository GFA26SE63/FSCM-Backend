using System.Globalization;
using FSCM.Modules.Ordering.Application;
using FSCM.Modules.Ordering.Domain.Allocation;
using FSCM.Modules.Ordering.Domain.Complaints;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.Modules.Ordering.Domain.Picking;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Modules.Ordering.Infrastructure;

internal sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(string code, CancellationToken ct) => db.Orders.FirstOrDefaultAsync(o => o.Id == code, ct);
    public Task<Order?> GetByClientRequestAsync(Guid id, CancellationToken ct) => db.Orders.FirstOrDefaultAsync(o => o.ClientRequestId == id, ct);
    public async Task<IReadOnlyList<Order>> SearchAsync(OrderFilter f, CancellationToken ct)
    {
        var q = db.Orders.AsQueryable();
        if (f.Status is not null) q = q.Where(o => o.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.RetailerCode)) q = q.Where(o => o.RetailerCode == f.RetailerCode);
        if (!string.IsNullOrWhiteSpace(f.SalesUserId)) q = q.Where(o => o.SalesUserId == f.SalesUserId);
        if (!string.IsNullOrWhiteSpace(f.Search)) q = q.Where(o => o.Id.Contains(f.Search) || o.RetailerName.ToLower().Contains(f.Search.ToLower()) || o.RetailerCode.Contains(f.Search));
        return await q.OrderByDescending(o => o.CreatedAtUtc).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<IssuedRow>> IssuedAsync(string warehouse, string sku, CancellationToken ct)
    {
        var rows = await (from o in db.Orders
                          where o.IssuedOn != null
                          from l in o.Lines
                          where l.Sku == sku
                          from a in l.Allocations
                          where a.WarehouseCode == warehouse
                          select new { o.RetailerCode, a.Quantity, o.IssuedOn }).ToListAsync(ct);
        return rows.Select(r => new IssuedRow(r.RetailerCode, r.Quantity, r.IssuedOn!.Value)).ToList();
    }
    public void Add(Order o) => db.Orders.Add(o);
}
internal sealed class PickingRepository(OrderingDbContext db) : IPickingRepository
{
    public Task<PickingList?> GetAsync(string code, CancellationToken ct) => db.PickingLists.FirstOrDefaultAsync(p => p.Id == code, ct);
    public async Task<IReadOnlyList<PickingList>> ByOrderAsync(string orderCode, CancellationToken ct) => await db.PickingLists.Where(p => p.OrderCode == orderCode).ToListAsync(ct);
    public async Task<IReadOnlyList<PickingList>> SearchAsync(string? warehouse, PickingStatus? status, CancellationToken ct) =>
        await db.PickingLists.Where(p => (warehouse == null || p.WarehouseCode == warehouse) && (status == null || p.Status == status)).OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);
    public void Add(PickingList p) => db.PickingLists.Add(p);
}
internal sealed class ComplaintRepository(OrderingDbContext db) : IComplaintRepository
{
    public Task<Complaint?> GetAsync(string code, CancellationToken ct) => db.Complaints.FirstOrDefaultAsync(c => c.Id == code, ct);
    public async Task<IReadOnlyList<Complaint>> SearchAsync(string? retailerCode, ComplaintStatus? status, CancellationToken ct) =>
        await db.Complaints.Where(c => (retailerCode == null || c.RetailerCode == retailerCode) && (status == null || c.Status == status)).OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct);
    public void Add(Complaint c) => db.Complaints.Add(c);
}
internal sealed class AllocationSettingsProvider(OrderingDbContext db) : IAllocationSettingsProvider
{
    public async Task<AllocationWeights> GetAsync(CancellationToken ct)
    {
        var s = await db.Settings.ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        decimal D(string k, decimal d) => s.TryGetValue(k, out var v) ? decimal.Parse(v, CultureInfo.InvariantCulture) : d;
        return new AllocationWeights(D("allocation.w_e", .5m), D("allocation.w_d", .3m), D("allocation.w_q", .2m));
    }
    public async Task SaveAsync(AllocationWeights w, CancellationToken ct)
    {
        var values = new Dictionary<string, string>
        {
            ["allocation.w_e"] = w.WE.ToString(CultureInfo.InvariantCulture), ["allocation.w_d"] = w.WD.ToString(CultureInfo.InvariantCulture), ["allocation.w_q"] = w.WQ.ToString(CultureInfo.InvariantCulture)
        };
        var existing = await db.Settings.ToDictionaryAsync(x => x.Key, ct);
        foreach (var (k, v) in values)
            if (existing.TryGetValue(k, out var row)) row.Value = v; else db.Settings.Add(new OrderingSetting { Key = k, Value = v });
        await db.SaveChangesAsync(ct);
    }
}
