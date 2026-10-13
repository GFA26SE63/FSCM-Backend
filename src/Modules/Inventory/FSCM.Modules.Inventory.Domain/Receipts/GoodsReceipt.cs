using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Inventory.Domain.Receipts;

public enum ReceiptStatus { Pending, Approved, Rejected }

public sealed class GoodsReceiptLine
{
    private GoodsReceiptLine() { }
    public GoodsReceiptLine(int lineNo, string sku, DateOnly expiry, int quantity, int labelCount)
    {
        Guard.Against(quantity <= 0, "receipt.qty", "Số lượng nhập phải lớn hơn 0.");
        Guard.Against(labelCount <= 0 || labelCount > quantity, "receipt.labels", "Số tem phải từ 1 đến số thùng nhập.");
        LineNo = lineNo; Sku = sku; Expiry = expiry; Quantity = quantity; LabelCount = labelCount;
    }
    public int LineNo { get; private set; }
    public string Sku { get; private set; } = "";
    public DateOnly Expiry { get; private set; }
    public int Quantity { get; private set; }
    public int LabelCount { get; private set; }
    /// <summary>Filled when the receipt is approved.</summary>
    public string? BatchCode { get; private set; }
    internal void Assign(string batchCode) => BatchCode = batchCode;
}

/// <summary>Phiếu nhập kho PN-yyMMdd-nn: Warehouse Keeper enters the lines, Admin approves → batches and QR labels are created.</summary>
public sealed class GoodsReceipt : AggregateRoot<string>
{
    private readonly List<GoodsReceiptLine> _lines = new();
    private GoodsReceipt() { }
    public GoodsReceipt(string code, string warehouseCode, IEnumerable<(string Sku, DateOnly Expiry, int Quantity, int LabelCount)> lines, string createdBy, DateTime nowUtc)
    {
        Id = code; WarehouseCode = warehouseCode; CreatedBy = createdBy; CreatedAtUtc = nowUtc; Status = ReceiptStatus.Pending;
        var n = 0; foreach (var l in lines) _lines.Add(new GoodsReceiptLine(++n, l.Sku, l.Expiry, l.Quantity, l.LabelCount));
        Guard.Against(_lines.Count == 0, "receipt.empty", "Phiếu nhập cần ít nhất một dòng.");
        Guard.Against(_lines.GroupBy(l => (l.Sku, l.Expiry)).Any(g => g.Count() > 1), "receipt.duplicate", "Cùng SKU và hạn sử dụng phải gộp thành một dòng (cùng một batch).");
    }
    public string WarehouseCode { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    public ReceiptStatus Status { get; private set; }
    public string? ApprovedBy { get; private set; }
    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines.AsReadOnly();

    public void Approve(string by, Func<GoodsReceiptLine, string> batchCodeOf)
    {
        Guard.Against(Status != ReceiptStatus.Pending, "receipt.state", "Phiếu đã được xử lý.");
        foreach (var l in _lines) l.Assign(batchCodeOf(l));
        Status = ReceiptStatus.Approved; ApprovedBy = by;
    }
    public void Reject(string by) { Guard.Against(Status != ReceiptStatus.Pending, "receipt.state", "Phiếu đã được xử lý."); Status = ReceiptStatus.Rejected; ApprovedBy = by; }
}
