using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Inventory.Contracts;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.Modules.Ordering.Domain.Picking;
using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Ordering.Application;

/// <summary>Màn "Soạn hàng" and the Warehouse Keeper mobile picking: scan QR labels until every line of the list is complete.</summary>
public sealed class PickingService(IPickingRepository pickings, IOrderRepository orders, IInventoryModule inventory, IOrderingUnitOfWork uow, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<PickingListDto>> ListAsync(string? warehouse, string? status, CancellationToken ct)
    {
        PickingStatus? st = Enum.TryParse<PickingStatus>(status, true, out var s) ? s : null;
        var wh = user.Role == Role.WarehouseKeeper ? user.WarehouseCode : warehouse;
        return (await pickings.SearchAsync(wh, st, ct)).Select(Map).ToList();
    }

    public async Task<PickingListDto> GetAsync(string code, CancellationToken ct) => Map(await Load(code, ct));

    /// <summary>
    /// Scan a label: it must be in stock in this warehouse and belong to a batch still missing on the list. Quantity defaults to
    /// min(label quantity, still needed); a smaller number splits the label ("tách tem"). When the list is complete the stock is issued;
    /// when every list of the order is done the order becomes "Đã xuất kho".
    /// </summary>
    public async Task<PickingListDto> ScanAsync(string code, ScanInput x, CancellationToken ct)
    {
        var list = await Load(code, ct);
        var now = clock.UtcNow;
        var label = await inventory.GetLabelAsync(x.LabelCode, ct) ?? throw new BusinessRuleException("label.not_found", $"Không có tem {x.LabelCode}.");
        Guard.Against(label.Warehouse != list.WarehouseCode, "picking.warehouse", $"Tem {label.LabelCode} thuộc kho {label.Warehouse}, phiếu {list.Id} soạn ở kho {list.WarehouseCode}.");
        Guard.Against(label.Status != "InStock", "picking.label", $"Tem {label.LabelCode} không còn trong kho ({label.Status}).");
        var line = list.Lines.FirstOrDefault(l => l.BatchCode == label.BatchCode && l.Remaining > 0)
            ?? throw new BusinessRuleException("picking.batch", $"Tem {label.LabelCode} thuộc batch {label.BatchCode} không nằm trong phiếu {list.Id} hoặc đã soạn đủ.");
        var qty = x.Quantity ?? Math.Min(label.Quantity, line.Remaining);
        Guard.Against(qty > label.Quantity, "picking.qty", $"Tem {label.LabelCode} chỉ có {label.Quantity} thùng.");

        var order = await orders.GetAsync(list.OrderCode, ct) ?? throw new BusinessRuleException("order.not_found", list.OrderCode);
        list.Scan(label.LabelCode, label.BatchCode, qty, user.FullName, now);
        order.StartPicking(user.UserId, user.FullName, now);

        if (list.IsComplete)
        {
            await inventory.IssueAsync(list.OrderCode, list.Id, list.Scans.Select(s => new IssueLine(s.BatchCode, s.LabelCode, s.Quantity)).ToList(), ct);
            var all = await pickings.ByOrderAsync(list.OrderCode, ct);
            if (all.Where(p => p.Status != PickingStatus.Cancelled).All(p => p.Status == PickingStatus.Done)) order.MarkIssued(user.UserId, user.FullName, now, clock.Today);
        }
        await uow.SaveChangesAsync(ct);
        return Map(list);
    }

    private async Task<PickingList> Load(string code, CancellationToken ct)
    {
        var p = await pickings.GetAsync(code, ct) ?? throw new BusinessRuleException("picking.not_found", $"Không có phiếu soạn {code}.");
        if (user.Role == Role.WarehouseKeeper && user.WarehouseCode != p.WarehouseCode) throw new UnauthorizedAccessException($"Phiếu {code} thuộc kho khác.");
        return p;
    }
    private static PickingListDto Map(PickingList p) => new(p.Id, p.OrderCode, p.WarehouseCode, p.Status.ToString(), p.CreatedAtUtc,
        p.Lines.Select(l => new PickingLineDto(l.LineNo, l.BatchCode, l.Sku, l.Quantity, l.Picked)).ToList(), p.Scans.Count);
}
