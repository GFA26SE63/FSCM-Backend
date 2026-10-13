using FSCM.SharedKernel.Domain;
using FSCM.SharedKernel.Ids;

namespace FSCM.Modules.Inventory.Domain.Batches;

public enum ExpiryState { Normal, NearExpiry, Expired }

/// <summary>
/// Stock is managed per batch (lot), never flat per SKU. Code = {WH}-{SKU}-{expiry ddMMyyyy}.
/// Held = soft hold while the order waits for approval; Reserved = after approval until issued.
/// Version is the optimistic-locking token: two Sales holding the same near-expiry batch at the same time
/// → the second SaveChanges fails and is retried against fresh stock (no negative inventory).
/// </summary>
public sealed class Batch : AggregateRoot<string>
{
    private Batch() { }
    public Batch(string warehouseCode, string sku, DateOnly expiry, int quantity)
    {
        Guard.Against(quantity <= 0, "batch.qty", "Số lượng nhập phải lớn hơn 0.");
        Id = IdFormat.Batch(warehouseCode, sku, expiry); WarehouseCode = warehouseCode; Sku = sku; Expiry = expiry; OnHand = quantity;
    }
    public string WarehouseCode { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public DateOnly Expiry { get; private set; }
    public int OnHand { get; private set; }
    public int Held { get; private set; }
    public int Reserved { get; private set; }
    public ExpiryState State { get; private set; }
    public int Available => OnHand - Held - Reserved;

    public void Receive(int qty) { Guard.Against(qty <= 0, "batch.qty", "Số lượng phải lớn hơn 0."); OnHand += qty; }
    public void Hold(int qty) { Guard.Against(qty > Available, "batch.negative", $"Batch {Id} chỉ còn {Available} thùng khả dụng."); Held += qty; }
    public void ReleaseHold(int qty) { Held = Math.Max(0, Held - qty); }
    public void Reserve(int qty) { Guard.Against(qty > Held, "batch.reserve", "Không thể reserve vượt phần đang giữ."); Held -= qty; Reserved += qty; }
    public void Issue(int qty) { Guard.Against(qty > Reserved, "batch.issue", "Xuất vượt phần đã reserve."); Reserved -= qty; OnHand -= qty; }
    public void TransferOut(int qty) { Guard.Against(qty > Available, "batch.negative", $"Batch {Id} chỉ còn {Available} thùng khả dụng."); OnHand -= qty; }
    /// <summary>Phiếu hủy PH: only an expired batch with nothing reserved. Returns the number of cases destroyed.</summary>
    public int Dispose()
    {
        Guard.Against(State != ExpiryState.Expired, "batch.dispose", "Chỉ hủy batch đã hết hạn.");
        Guard.Against(Reserved > 0, "batch.dispose", "Batch còn hàng đã reserve cho đơn – xử lý đơn trước khi hủy.");
        var qty = OnHand; OnHand = 0; Held = 0; return qty;
    }
    /// <summary>Order cancelled after approval: reserved stock goes back to available (not back to "held").</summary>
    public void Unreserve(int qty) { Reserved = Math.Max(0, Reserved - qty); }

    /// <summary>Called by the nightly date tracker.</summary>
    public void Track(DateOnly today, int nearExpiryDays) =>
        State = Expiry <= today ? ExpiryState.Expired : (Expiry.DayNumber - today.DayNumber <= nearExpiryDays ? ExpiryState.NearExpiry : ExpiryState.Normal);
}
