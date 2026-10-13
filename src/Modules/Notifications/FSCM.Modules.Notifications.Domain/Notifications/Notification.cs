using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Notifications.Domain.Notifications;

/// <summary>In-app notification (bell icon in Portal and Mobile). A push message with the same text goes to the user's registered devices.</summary>
public sealed class Notification : Entity<Guid>
{
    private Notification() { }
    public Notification(string userId, string type, string title, string body, string? reference, DateTime nowUtc)
    {
        Guard.Against(string.IsNullOrWhiteSpace(userId), "notification.user", "Thiếu người nhận.");
        Id = Guid.NewGuid(); UserId = userId; Type = type; Title = title; Body = body; Reference = reference; CreatedAtUtc = nowUtc;
    }
    public string UserId { get; private set; } = "";
    /// <summary>Event name, e.g. OrderApproved – the client uses it to pick an icon and deep link.</summary>
    public string Type { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    /// <summary>Order / redemption / receivable code the notification is about.</summary>
    public string? Reference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public bool IsRead => ReadAtUtc is not null;
    public void MarkRead(DateTime nowUtc) { ReadAtUtc ??= nowUtc; }
}

/// <summary>Message templates per event (Vietnamese UI text).</summary>
public static class Templates
{
    public static (string Title, string Body) OrderSubmitted(string order, string retailer, decimal net) => ("Đơn mới chờ duyệt", $"Đơn {order} của {retailer} ({net:N0}đ) đang chờ duyệt.");
    public static (string Title, string Body) OrderApproved(string order) => ("Đơn đã được duyệt", $"Đơn {order} đã được duyệt và chuyển sang soạn hàng.");
    public static (string Title, string Body) OrderRejected(string order, string reason) => ("Đơn bị từ chối", $"Đơn {order} bị từ chối: {reason}");
    public static (string Title, string Body) OrderCancelled(string order) => ("Đơn đã hủy", $"Đơn {order} đã được hủy, hàng tạm giữ đã trả lại kho.");
    public static (string Title, string Body) OrderShipped(string order, string plate, string driver, string phone) => ("Đơn đang được giao", $"Đơn {order} đang giao bởi {driver} ({phone}), xe {plate}. Quét QR trên phiếu giao khi nhận hàng.");
    public static (string Title, string Body) OrderDelivered(string order) => ("Đơn đã giao", $"Retailer đã xác nhận nhận hàng đơn {order}.");
    public static (string Title, string Body) ReceivableOverdue(string order, decimal amount, int days) => ("Công nợ quá hạn", $"Đơn {order} ({amount:N0}đ) quá hạn {days} ngày.");
    public static (string Title, string Body) CreditPaid(string order, decimal amount) => ("Đã thu công nợ", $"Đơn {order} đã thu đủ {amount:N0}đ.");
    public static (string Title, string Body) BatchExpired(string batch, int qty) => ("Batch hết hạn", $"Batch {batch} đã hết hạn, còn {qty} thùng cần xử lý (phiếu hủy).");
    public static (string Title, string Body) RotationProposals(int count) => ("Đề xuất luân chuyển cận hạn", $"Có {count} đề xuất luân chuyển hàng cận hạn chờ duyệt.");
    public static (string Title, string Body) TransferCreated(string code, string from, string sku, int qty) => ("Hàng đang chuyển tới kho", $"Phiếu {code}: {qty} thùng {sku} từ {from} đang vận chuyển, quét tem để nhận.");
    public static (string Title, string Body) GiftRequested(string code, string retailer, string gift) => ("Yêu cầu đổi quà mới", $"{retailer} yêu cầu đổi {gift} ({code}).");
    public static (string Title, string Body) GiftApproved(string code, string gift) => ("Yêu cầu đổi quà đã duyệt", $"Yêu cầu {code} đổi {gift} đã được duyệt.");
    public static (string Title, string Body) GiftRejected(string code, string gift, int refunded, string reason) => ("Yêu cầu đổi quà bị từ chối", $"Yêu cầu {code} đổi {gift} bị từ chối ({reason}). Đã hoàn {refunded} điểm.");
    public static (string Title, string Body) GiftDelivered(string code, string gift) => ("Quà đang được giao", $"Quà {gift} của yêu cầu {code} đang được giao.");
    public static (string Title, string Body) RetailerRegistered(string name, string code) => ("Điểm bán đã được duyệt", $"{name} đã được duyệt, mã retailer {code}.");
    public static (string Title, string Body) RetailerRejected(string name, string reason) => ("Khai báo điểm bán bị từ chối", $"{name} bị từ chối: {reason}");
    public static (string Title, string Body) ComplaintCreated(string code, string order) => ("Khiếu nại mới", $"Khiếu nại {code} về đơn {order} cần xử lý.");
}
