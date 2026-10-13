using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Inventory.Domain.Batches;

public enum LabelStatus { InStock, InTransit, Delivered, Split, Disposed }

/// <summary>QR label (tem) TEM-000000 stuck on a stack of cases. Picking can split it into a child label.</summary>
public sealed class Label : AggregateRoot<string>
{
    private Label() { }
    public Label(string code, string batchCode, int quantity, string? parent = null) { Id = code; BatchCode = batchCode; Quantity = quantity; ParentCode = parent; Status = LabelStatus.InStock; }
    public string BatchCode { get; private set; } = "";
    public int Quantity { get; private set; }
    public string? ParentCode { get; private set; }
    public string? PickingCode { get; private set; }
    public LabelStatus Status { get; private set; }

    /// <summary>Take part of the label for a picking line. Parent keeps its code with the remainder.</summary>
    public Label Split(string childCode, int qty, string pickingCode)
    {
        Guard.Against(qty <= 0 || qty >= Quantity, "label.split", $"Tách {qty} từ tem {Id} ({Quantity} thùng) không hợp lệ.");
        Quantity -= qty;
        var child = new Label(childCode, BatchCode, qty, Id) { PickingCode = pickingCode, Status = LabelStatus.InTransit };
        return child;
    }
    public void Ship(string pickingCode) { PickingCode = pickingCode; Status = LabelStatus.InTransit; }
    public void MarkDelivered() => Status = LabelStatus.Delivered;
    public void MarkDisposed() => Status = LabelStatus.Disposed;
    public void MoveTo(string newBatchCode) { BatchCode = newBatchCode; Status = LabelStatus.InStock; } // transfer keeps the TEM code
}
