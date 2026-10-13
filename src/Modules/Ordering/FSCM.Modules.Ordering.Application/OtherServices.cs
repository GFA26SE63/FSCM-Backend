using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Ordering.Domain.Allocation;
using FSCM.Modules.Ordering.Domain.Complaints;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.Modules.Partners.Contracts;
using FSCM.Modules.Pricing.Contracts;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Ordering.Application;

/// <summary>Khiếu nại KN-yyMMdd-nn.</summary>
public sealed class ComplaintService(IComplaintRepository repo, IOrderRepository orders, IOrderingUnitOfWork uow, IdGenerator ids, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<ComplaintDto>> ListAsync(string? status, CancellationToken ct)
    {
        ComplaintStatus? st = Enum.TryParse<ComplaintStatus>(status, true, out var s) ? s : null;
        return (await repo.SearchAsync(user.Role == Role.Retailer ? user.RetailerCode : null, st, ct)).Select(Map).ToList();
    }

    public async Task<string> CreateAsync(ComplaintInput x, CancellationToken ct)
    {
        var o = await orders.GetAsync(x.OrderCode, ct) ?? throw new BusinessRuleException("order.not_found", $"Không có đơn {x.OrderCode}.");
        Guard.Against(user.Role == Role.Retailer && user.RetailerCode != o.RetailerCode, "complaint.owner", "Chỉ khiếu nại được đơn của mình.");
        Guard.Against(o.Status is not (OrderStatus.Delivered or OrderStatus.Completed), "complaint.state", "Chỉ khiếu nại đơn đã giao.");
        var code = await ids.DocumentAsync(DocumentType.Complaint, ct);
        repo.Add(new Complaint(code, o.Id, o.RetailerCode, x.Content, clock.UtcNow));
        uow.Enqueue(new ComplaintCreated(code, o.Id, o.RetailerCode));
        await uow.SaveChangesAsync(ct); return code;
    }

    public async Task ResolveAsync(string code, ResolveComplaintInput x, CancellationToken ct)
    {
        var c = await repo.GetAsync(code, ct) ?? throw new BusinessRuleException("complaint.not_found", $"Không có khiếu nại {code}.");
        c.Resolve(x.Result, user.FullName, clock.UtcNow); await uow.SaveChangesAsync(ct);
    }
    private static ComplaintDto Map(Complaint c) => new(c.Id, c.OrderCode, c.RetailerCode, c.Content, c.Status.ToString(), c.Result, c.CreatedAtUtc, c.ResolvedAtUtc);
}

/// <summary>Weights of the allocation score (Portal "Cấu hình hệ thống"); must sum to 1.</summary>
public sealed class AllocationSettingsService(IAllocationSettingsProvider provider, Logistics.Contracts.ILogisticsModule logistics)
{
    public async Task<AllocationSettingsDto> GetAsync(CancellationToken ct)
    {
        var w = await provider.GetAsync(ct); var (dmax, _) = await logistics.LimitsAsync(ct);
        return new AllocationSettingsDto(w.WE, w.WD, w.WQ, dmax);
    }
    public async Task SaveAsync(AllocationSettingsDto x, CancellationToken ct)
    {
        Guard.Against(Math.Abs(x.WE + x.WD + x.WQ - 1m) > 0.001m || x.WE < 0 || x.WD < 0 || x.WQ < 0, "allocation.weights", "Ba trọng số phải không âm và có tổng bằng 1.");
        await provider.SaveAsync(new AllocationWeights(x.WE, x.WD, x.WQ), ct);
    }
}

public sealed record PriceRow(string Sku, decimal UnitPrice);
public sealed record StockRow(string Sku, int AvailableCases);
public sealed record SyncSnapshotDto(DateTime ServerTimeUtc, IReadOnlyList<RetailerDto> Retailers, IReadOnlyList<ProductDto> Products, IReadOnlyList<PriceRow> Prices,
    IReadOnlyList<PromotionSnapshotDto> Promotions, IReadOnlyList<StockRow> Stock);

/// <summary>GET /sync/snapshot?since= – everything the Sales app caches in SQLite for offline order taking.</summary>
public sealed class SyncSnapshotService(IPartnersModule partners, ICatalogModule catalog, IPricingModule pricing, IInventoryModule inventory, ICurrentUser user, IClock clock)
{
    public async Task<SyncSnapshotDto> GetAsync(DateTime? sinceUtc, CancellationToken ct)
    {
        var retailers = user.Role == Role.Sales ? await partners.RetailersOfSalesAsync(user.UserId, sinceUtc, ct) : Array.Empty<RetailerDto>();   // only retailers of the Sales' own area
        var products = await catalog.PublishedProductsAsync(ct);
        var stock = new List<StockRow>();
        foreach (var p in products)
            stock.Add(new StockRow(p.Sku, (await inventory.AllocatableBatchesAsync(p.Sku, ct)).Where(b => !b.CentralWarehouse).Sum(b => Math.Max(0, b.Available))));
        return new SyncSnapshotDto(clock.UtcNow, retailers, products, products.Select(p => new PriceRow(p.Sku, p.BasePrice)).ToList(),
            await pricing.ActivePromotionsAsync(clock.Today, ct), stock);
    }
}

public sealed class OrderingModule(IOrderRepository orders) : IOrderingModule
{
    public async Task<IReadOnlyList<OutboundRow>> OutboundByRetailerAsync(string warehouse, string sku, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        (await orders.IssuedAsync(warehouse, sku, ct)).GroupBy(r => r.RetailerCode)
            .Select(g => new OutboundRow(g.Key, g.Where(r => r.IssuedOn >= from && r.IssuedOn <= to).Sum(r => r.Quantity), g.Min(r => r.IssuedOn))).ToList();
}
