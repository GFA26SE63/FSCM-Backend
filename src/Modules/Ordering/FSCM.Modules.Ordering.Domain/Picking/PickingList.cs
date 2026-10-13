using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Ordering.Domain.Picking;

public enum PickingStatus { Open, InProgress, Done, Cancelled }

public sealed class PickingLine
{
    private PickingLine() { }
    public PickingLine(int lineNo, string batchCode, string sku, int quantity) { LineNo = lineNo; BatchCode = batchCode; Sku = sku; Quantity = quantity; }
    public int LineNo { get; private set; }
    public string BatchCode { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public int Quantity { get; private set; }
    public int Picked { get; private set; }
    public int Remaining => Quantity - Picked;
    internal void Add(int qty) => Picked += qty;
}

/// <summary>One scan of a QR label (TEM) during picking. A label may be split when only part of it is needed.</summary>
public sealed class PickingScan
{
    private PickingScan() { }
    public PickingScan(int seq, string labelCode, string batchCode, int quantity, string scannedBy, DateTime atUtc) { Seq = seq; LabelCode = labelCode; BatchCode = batchCode; Quantity = quantity; ScannedBy = scannedBy; AtUtc = atUtc; }
    public int Seq { get; private set; }
    public string LabelCode { get; private set; } = "";
    public string BatchCode { get; private set; } = "";
    public int Quantity { get; private set; }
    public string ScannedBy { get; private set; } = "";
    public DateTime AtUtc { get; private set; }
}

/// <summary>Picking list {order}{A/B/C}: one per warehouse that ships part of the order. The Warehouse Keeper scans labels until every line is complete.</summary>
public sealed class PickingList : AggregateRoot<string>
{
    private readonly List<PickingLine> _lines = new();
    private readonly List<PickingScan> _scans = new();
    private PickingList() { }
    public PickingList(string orderCode, int warehouseIndex, string warehouseCode, IEnumerable<(string BatchCode, string Sku, int Quantity)> lines, DateTime nowUtc)
    {
        Id = IdFormat.Picking(orderCode, warehouseIndex); OrderCode = orderCode; WarehouseCode = warehouseCode; CreatedAtUtc = nowUtc; Status = PickingStatus.Open;
        var n = 0; foreach (var l in lines) _lines.Add(new PickingLine(++n, l.BatchCode, l.Sku, l.Quantity));
        Guard.Against(_lines.Count == 0, "picking.empty", "Phiếu soạn hàng không có dòng nào.");
    }
    public string OrderCode { get; private set; } = "";
    public string WarehouseCode { get; private set; } = "";
    public PickingStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<PickingLine> Lines => _lines.AsReadOnly();
    public IReadOnlyCollection<PickingScan> Scans => _scans.AsReadOnly();
    public bool IsComplete => _lines.All(l => l.Remaining == 0);

    /// <summary>Registers a scan. Caller has already checked that the label belongs to <paramref name="batchCode"/> and holds at least <paramref name="quantity"/>.</summary>
    public void Scan(string labelCode, string batchCode, int quantity, string by, DateTime nowUtc)
    {
        Guard.Against(Status is PickingStatus.Done or PickingStatus.Cancelled, "picking.state", $"Phiếu {Id} đã đóng.");
        Guard.Against(quantity <= 0, "picking.qty", "Số lượng quét phải lớn hơn 0.");
        var line = _lines.FirstOrDefault(l => l.BatchCode == batchCode && l.Remaining > 0)
            ?? throw new BusinessRuleException("picking.batch", $"Tem {labelCode} thuộc batch {batchCode} không nằm trong phiếu {Id} hoặc đã soạn đủ.");
        Guard.Against(quantity > line.Remaining, "picking.over", $"Batch {batchCode} chỉ cần thêm {line.Remaining} thùng.");
        line.Add(quantity); _scans.Add(new PickingScan(_scans.Count + 1, labelCode, batchCode, quantity, by, nowUtc));
        Status = IsComplete ? PickingStatus.Done : PickingStatus.InProgress;
        if (IsComplete) CompletedAtUtc = nowUtc;
    }
    public void Cancel() { Guard.Against(Status != PickingStatus.Open, "picking.state", "Chỉ hủy phiếu chưa soạn."); Status = PickingStatus.Cancelled; }
}
