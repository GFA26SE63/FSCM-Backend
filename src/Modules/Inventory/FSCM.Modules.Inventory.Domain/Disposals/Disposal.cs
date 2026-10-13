using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Inventory.Domain.Disposals;

/// <summary>Phiếu hủy PH-yyMMdd-nn: destruction of an expired batch (stock goes to zero, labels become Disposed).</summary>
public sealed class Disposal : AggregateRoot<string>
{
    private Disposal() { }
    public Disposal(string code, string batchCode, string warehouseCode, string sku, int quantity, string reason, string createdBy, DateTime nowUtc)
    {
        Guard.Against(quantity <= 0, "disposal.qty", "Batch không còn hàng để hủy.");
        Id = code; BatchCode = batchCode; WarehouseCode = warehouseCode; Sku = sku; Quantity = quantity; Reason = reason; CreatedBy = createdBy; CreatedAtUtc = nowUtc;
    }
    public string BatchCode { get; private set; } = "";
    public string WarehouseCode { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public int Quantity { get; private set; }
    public string Reason { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
}
