using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Inventory.Domain.Transfers;

public enum TransferKind { Replenishment, NearExpiryRotation }
public enum TransferStatus { InTransit, Received, Cancelled }

/// <summary>Phiếu điều chuyển DC-yyMMdd-nn. Replenishment = central → regional when low; NearExpiryRotation = lateral move approved from a rotation proposal.</summary>
public sealed class Transfer : AggregateRoot<string>
{
    private Transfer() { }
    public Transfer(string code, TransferKind kind, string from, string to, string sku, string sourceBatch, DateOnly expiry, int qty, string labelCode, string createdBy, decimal? score = null)
    {
        Guard.Against(from == to, "transfer.same", "Kho đến phải khác kho đi.");
        Id = code; Kind = kind; FromWarehouse = from; ToWarehouse = to; Sku = sku; SourceBatch = sourceBatch; Expiry = expiry; Quantity = qty; LabelCode = labelCode; CreatedBy = createdBy; Score = score;
        Status = TransferStatus.InTransit; CreatedAtUtc = DateTime.UtcNow;
    }
    public TransferKind Kind { get; private set; }
    public string FromWarehouse { get; private set; } = "";
    public string ToWarehouse { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public string SourceBatch { get; private set; } = "";
    public DateOnly Expiry { get; private set; }
    public int Quantity { get; private set; }
    public string LabelCode { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public decimal? Score { get; private set; }
    public TransferStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    /// <summary>Batch at destination = {to}-{sku}-{expiry}; the label code stays the same for traceability.</summary>
    public string DestinationBatch => SharedKernel.Ids.IdFormat.Batch(ToWarehouse, Sku, Expiry);
    public void Cancel() { Guard.Against(Status != TransferStatus.InTransit, "transfer.state", "Chỉ hủy phiếu đang vận chuyển."); Status = TransferStatus.Cancelled; }
    public void Receive() { Guard.Against(Status != TransferStatus.InTransit, "transfer.state", "Phiếu không ở trạng thái đang vận chuyển."); Status = TransferStatus.Received; ReceivedAtUtc = DateTime.UtcNow; }
}
