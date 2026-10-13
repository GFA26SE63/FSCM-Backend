using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Catalog.Contracts;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Inventory.Domain.Batches;
using FSCM.Modules.Inventory.Domain.Disposals;
using FSCM.Modules.Inventory.Domain.Receipts;
using FSCM.Modules.Inventory.Domain.Transfers;
using FSCM.Modules.Inventory.Domain.Warehouses;
using FSCM.Modules.Ordering.Contracts;
using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Inventory.Application.Stock;

public sealed record WarehouseDto(string Code, string Name, string Type, string Address, double Lat, double Lng, int CapacityCases, string? KeeperUserId, bool Active);
public sealed record WarehouseInput(string Code, string Name, string Type, string Address, double Lat, double Lng, int CapacityCases, string? KeeperUserId, bool Active = true);
public sealed record BatchDto(string Code, string Warehouse, string Sku, DateOnly Expiry, int OnHand, int Held, int Reserved, int Available, string State, int RemainingDays);
public sealed record ReceiptLineInput(string Sku, DateOnly Expiry, int Quantity, int LabelCount);
public sealed record ReceiptInput(string Warehouse, IReadOnlyList<ReceiptLineInput> Lines);
public sealed record ReceiptLineDto(int LineNo, string Sku, DateOnly Expiry, int Quantity, int LabelCount, string? BatchCode);
public sealed record ReceiptDto(string Code, string Warehouse, string Status, string CreatedBy, DateTime CreatedAtUtc, string? ApprovedBy, IReadOnlyList<ReceiptLineDto> Lines);
public sealed record LabelLookupDto(string Code, string Kind, string BatchCode, string Sku, string Warehouse, int Quantity, string Status, string? ParentCode, string? PickingCode, string? TransferCode);
public sealed record TransferDto(string Code, string Kind, string From, string To, string Sku, string SourceBatch, DateOnly Expiry, int Quantity, string LabelCode, string Status, DateTime CreatedAtUtc, DateTime? ReceivedAtUtc, decimal? Score);
public sealed record ReplenishInput(string From, string To, string BatchCode, int Quantity);
public sealed record DisposalInput(string BatchCode, string Reason);
public sealed record DisposalDto(string Code, string BatchCode, string Warehouse, string Sku, int Quantity, string Reason, string CreatedBy, DateTime CreatedAtUtc);

public sealed class WarehouseService(IWarehouseRepository repo, IInventoryUnitOfWork uow)
{
    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(CancellationToken ct) => (await repo.AllAsync(ct)).Select(Map).OrderBy(w => w.Code).ToList();
    public async Task<string> SaveAsync(WarehouseInput x, CancellationToken ct)
    {
        var w = await repo.GetAsync(x.Code, ct);
        if (w is null) { Guard.Against(!Enum.TryParse<WarehouseType>(x.Type, true, out var t), "warehouse.type", "Loại kho: Central hoặc Regional."); w = new Warehouse(x.Code, x.Name, Enum.Parse<WarehouseType>(x.Type, true), x.Address, x.Lat, x.Lng); repo.Add(w); }
        w.Update(x.Name, x.Address, x.Lat, x.Lng, x.CapacityCases, x.Active); w.AssignKeeper(x.KeeperUserId);
        await uow.SaveChangesAsync(ct); return w.Id;
    }
    private static WarehouseDto Map(Warehouse w) => new(w.Id, w.Name, w.Type.ToString(), w.Address, w.Lat, w.Lng, w.CapacityCases, w.KeeperUserId, w.Active);
}

public sealed class BatchService(IBatchRepository repo, ICatalogModule catalog, IClock clock)
{
    public async Task<PagedResult<BatchDto>> SearchAsync(PageRequest page, string? warehouse, string? sku, string? state, CancellationToken ct)
    {
        ExpiryState? st = Enum.TryParse<ExpiryState>(state, true, out var s) ? s : null;
        var today = clock.Today;
        return (await repo.SearchAsync(warehouse, sku, st, page.Search, ct)).OrderBy(b => b.Expiry).ThenBy(b => b.Id, StringComparer.Ordinal)
            .Select(b => new BatchDto(b.Id, b.WarehouseCode, b.Sku, b.Expiry, b.OnHand, b.Held, b.Reserved, b.Available, b.State.ToString(), b.Expiry.DayNumber - today.DayNumber)).Page(page);
    }
}

public sealed class GoodsReceiptService(IGoodsReceiptRepository receipts, IBatchRepository batches, ILabelRepository labels, IWarehouseRepository warehouses,
    ICatalogModule catalog, IInventoryUnitOfWork uow, IdGenerator ids, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<ReceiptDto>> ListAsync(string? warehouse, string? status, CancellationToken ct)
    {
        ReceiptStatus? st = Enum.TryParse<ReceiptStatus>(status, true, out var s) ? s : null;
        var wh = user.Role == Role.WarehouseKeeper ? user.WarehouseCode : warehouse;
        return (await receipts.SearchAsync(wh, st, ct)).Select(Map).ToList();
    }

    /// <summary>Warehouse Keeper enters a receipt; expiry must be in the future and within the product shelf life.</summary>
    public async Task<string> CreateAsync(ReceiptInput x, CancellationToken ct)
    {
        var wh = user.Role == Role.WarehouseKeeper ? user.WarehouseCode ?? x.Warehouse : x.Warehouse;
        Guard.Against(await warehouses.GetAsync(wh, ct) is null, "warehouse.not_found", $"Không có kho {wh}.");
        foreach (var l in x.Lines)
        {
            var p = await catalog.GetProductAsync(l.Sku, ct) ?? throw new BusinessRuleException("product.not_found", $"Không có sản phẩm {l.Sku}.");
            Guard.Against(p.Status != "Published", "product.not_published", $"{l.Sku} chưa publish.");
            Guard.Against(l.Expiry <= clock.Today, "receipt.expiry", $"HSD của {l.Sku} đã qua.");
            Guard.Against(l.Expiry > clock.Today.AddDays(p.ShelfLifeDays), "receipt.expiry", $"HSD của {l.Sku} vượt hạn dùng {p.ShelfLifeDays} ngày của sản phẩm.");
        }
        var code = await ids.DocumentAsync(DocumentType.GoodsReceipt, ct);
        receipts.Add(new GoodsReceipt(code, wh, x.Lines.Select(l => (l.Sku, l.Expiry, l.Quantity, l.LabelCount)), user.FullName, clock.UtcNow));
        await uow.SaveChangesAsync(ct); return code;
    }

    /// <summary>Admin approves → batch {WH}-{SKU}-{expiry} is created (or increased: same warehouse, SKU and expiry = same batch) and QR labels are printed.</summary>
    public async Task ApproveAsync(string code, CancellationToken ct)
    {
        var r = await receipts.GetAsync(code, ct) ?? throw new BusinessRuleException("receipt.not_found", $"Không có phiếu {code}.");
        r.Approve(user.FullName, l => IdFormat.Batch(r.WarehouseCode, l.Sku, l.Expiry));
        foreach (var l in r.Lines)
        {
            var batch = await batches.GetAsync(l.BatchCode!, ct);
            if (batch is null) { batch = new Batch(r.WarehouseCode, l.Sku, l.Expiry, l.Quantity); batches.Add(batch); } else batch.Receive(l.Quantity);
            var each = l.Quantity / l.LabelCount; var extra = l.Quantity % l.LabelCount;
            for (var i = 0; i < l.LabelCount; i++) labels.Add(new Label(await ids.LabelAsync(ct), batch.Id, each + (i == 0 ? extra : 0)));
        }
        await uow.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(string code, CancellationToken ct)
    {
        (await receipts.GetAsync(code, ct) ?? throw new BusinessRuleException("receipt.not_found", $"Không có phiếu {code}.")).Reject(user.FullName);
        await uow.SaveChangesAsync(ct);
    }
    private static ReceiptDto Map(GoodsReceipt r) => new(r.Id, r.WarehouseCode, r.Status.ToString(), r.CreatedBy, r.CreatedAtUtc, r.ApprovedBy,
        r.Lines.Select(l => new ReceiptLineDto(l.LineNo, l.Sku, l.Expiry, l.Quantity, l.LabelCount, l.BatchCode)).ToList());
}

public sealed class LabelService(ILabelRepository labels, IBatchRepository batches, ITransferRepository transfers)
{
    /// <summary>Scan / lookup a label: a stock label, or the label travelling with a transfer.</summary>
    public async Task<LabelLookupDto> LookupAsync(string code, CancellationToken ct)
    {
        if (await labels.GetAsync(code, ct) is { } l)
        {
            var b = await batches.GetAsync(l.BatchCode, ct);
            return new LabelLookupDto(l.Id, "Stock", l.BatchCode, b?.Sku ?? "", b?.WarehouseCode ?? "", l.Quantity, l.Status.ToString(), l.ParentCode, l.PickingCode, null);
        }
        if (await transfers.ByLabelAsync(code, ct) is { } t)
            return new LabelLookupDto(code, "Transfer", t.SourceBatch, t.Sku, t.ToWarehouse, t.Quantity, t.Status.ToString(), null, null, t.Id);
        throw new BusinessRuleException("label.not_found", $"Không có tem {code}.");
    }
}

public sealed class TransferService(ITransferRepository transfers, IBatchRepository batches, IWarehouseRepository warehouses, IInventoryUnitOfWork uow,
    IdGenerator ids, ICurrentUser user)
{
    public async Task<IReadOnlyList<TransferDto>> RecentAsync(CancellationToken ct) => (await transfers.RecentAsync(100, ct)).Select(Map).ToList();

    /// <summary>Replenishment: central warehouse sends stock to a regional warehouse. The transfer starts "Đang vận chuyển"; stock leaves the source batch now.</summary>
    public async Task<string> CreateReplenishmentAsync(ReplenishInput x, CancellationToken ct)
    {
        var from = await warehouses.GetAsync(x.From, ct) ?? throw new BusinessRuleException("warehouse.not_found", x.From);
        var to = await warehouses.GetAsync(x.To, ct) ?? throw new BusinessRuleException("warehouse.not_found", x.To);
        Guard.Against(from.Type != WarehouseType.Central || to.Type != WarehouseType.Regional, "transfer.kind", "Bổ sung hàng đi từ kho tổng đến kho vùng.");
        var b = await batches.GetAsync(x.BatchCode, ct) ?? throw new BusinessRuleException("batch.not_found", x.BatchCode);
        Guard.Against(b.WarehouseCode != from.Id, "transfer.batch", $"Batch {b.Id} không thuộc kho {from.Id}.");
        b.TransferOut(x.Quantity);
        var code = await ids.DocumentAsync(DocumentType.Transfer, ct);
        transfers.Add(new Transfer(code, TransferKind.Replenishment, from.Id, to.Id, b.Sku, b.Id, b.Expiry, x.Quantity, await ids.LabelAsync(ct), user.FullName));
        await uow.SaveChangesAsync(ct); return code;
    }

    /// <summary>Keeper of the destination warehouse scans the transfer label: destination batch is created / increased and the transfer is closed.</summary>
    public async Task ReceiveAsync(string code, string scannedLabel, CancellationToken ct)
    {
        var t = await transfers.GetAsync(code, ct) ?? throw new BusinessRuleException("transfer.not_found", $"Không có phiếu {code}.");
        Guard.Against(!string.Equals(t.LabelCode, scannedLabel, StringComparison.Ordinal), "transfer.label", $"Tem {scannedLabel} không thuộc phiếu {code}.");
        Guard.Against(user.Role == Role.WarehouseKeeper && user.WarehouseCode != t.ToWarehouse, "transfer.warehouse", "Chỉ thủ kho nhận mới được xác nhận phiếu này.");
        t.Receive();
        var dest = await batches.GetAsync(t.DestinationBatch, ct);
        if (dest is null) batches.Add(new Batch(t.ToWarehouse, t.Sku, t.Expiry, t.Quantity)); else dest.Receive(t.Quantity);
        uow.Enqueue(new TransferReceived(t.Id, t.ToWarehouse, t.Sku, t.Quantity, user.FullName));
        await uow.SaveChangesAsync(ct);
    }

    private static TransferDto Map(Transfer t) => new(t.Id, t.Kind.ToString(), t.FromWarehouse, t.ToWarehouse, t.Sku, t.SourceBatch, t.Expiry, t.Quantity, t.LabelCode, t.Status.ToString(), t.CreatedAtUtc, t.ReceivedAtUtc, t.Score);
}

public sealed class DisposalService(IDisposalRepository repo, IBatchRepository batches, ILabelRepository labels, IInventoryUnitOfWork uow, IdGenerator ids, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<DisposalDto>> ListAsync(string? warehouse, CancellationToken ct) =>
        (await repo.RecentAsync(user.Role == Role.WarehouseKeeper ? user.WarehouseCode : warehouse, 100, ct))
        .Select(d => new DisposalDto(d.Id, d.BatchCode, d.WarehouseCode, d.Sku, d.Quantity, d.Reason, d.CreatedBy, d.CreatedAtUtc)).ToList();

    public async Task<string> CreateAsync(DisposalInput x, CancellationToken ct)
    {
        var b = await batches.GetAsync(x.BatchCode, ct) ?? throw new BusinessRuleException("batch.not_found", x.BatchCode);
        Guard.Against(user.Role == Role.WarehouseKeeper && user.WarehouseCode != b.WarehouseCode, "disposal.warehouse", "Chỉ hủy hàng thuộc kho của bạn.");
        b.Track(clock.Today, 0);
        var qty = b.Dispose();
        foreach (var l in await labels.ByBatchAsync(b.Id, ct)) l.MarkDisposed();
        var code = await ids.DocumentAsync(DocumentType.Disposal, ct);
        repo.Add(new Disposal(code, b.Id, b.WarehouseCode, b.Sku, qty, x.Reason, user.FullName, clock.UtcNow));
        await uow.SaveChangesAsync(ct); return code;
    }
}

/// <summary>Inventory ← OrderApproved / OrderCancelled / OrderRejected (SPEC §4).</summary>
public sealed class OrderApprovedHandler(IInventoryModule inventory) : IIntegrationEventHandler<OrderApproved>
{ public Task HandleAsync(OrderApproved e, CancellationToken ct = default) => inventory.ReserveAsync(e.OrderCode, ct); }
public sealed class OrderCancelledHandler(IInventoryModule inventory) : IIntegrationEventHandler<OrderCancelled>
{ public Task HandleAsync(OrderCancelled e, CancellationToken ct = default) => e.StockWasHeld ? inventory.ReleaseAsync(e.OrderCode, ct) : Task.CompletedTask; }
public sealed class OrderRejectedHandler(IInventoryModule inventory) : IIntegrationEventHandler<OrderRejected>
{ public Task HandleAsync(OrderRejected e, CancellationToken ct = default) => e.StockWasHeld ? inventory.ReleaseAsync(e.OrderCode, ct) : Task.CompletedTask; }
