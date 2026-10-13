using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Ordering.Domain.Orders;

public enum OrderStatus { Pending, Approved, Picking, Shipped, Delivering, Delivered, Completed, Rejected, Cancelled }
public enum PaymentMethod { COD, Prepaid, Credit }
public enum OrderSource { Online, Offline }

/// <summary>One batch (lot) assigned to an order line by the allocation engine. Snapshot of distance / transit / cost at allocation time.</summary>
public sealed class OrderAllocation
{
    private OrderAllocation() { }
    public OrderAllocation(string batchCode, string warehouseCode, int quantity, decimal distanceKm, int transitDays, DateOnly eta, int remainingDays, decimal score, decimal tripCost)
    { BatchCode = batchCode; WarehouseCode = warehouseCode; Quantity = quantity; DistanceKm = distanceKm; TransitDays = transitDays; Eta = eta; RemainingDays = remainingDays; Score = score; TripCost = tripCost; }
    public string BatchCode { get; private set; } = "";
    public string WarehouseCode { get; private set; } = "";
    public int Quantity { get; private set; }
    public decimal DistanceKm { get; private set; }
    public int TransitDays { get; private set; }
    public DateOnly Eta { get; private set; }
    public int RemainingDays { get; private set; }
    public decimal Score { get; private set; }
    public decimal TripCost { get; private set; }
    /// <summary>KMB percent in force for this batch when the order was placed (0 if none).</summary>
    public decimal BatchDiscountPercent { get; private set; }
    public decimal BatchDiscountAmount { get; private set; }
    internal void ApplyBatchDiscount(decimal percent, decimal amount) { BatchDiscountPercent = percent; BatchDiscountAmount = amount; }
}

public sealed class OrderLine
{
    private readonly List<OrderAllocation> _allocations = new();
    private OrderLine() { }
    public OrderLine(int lineNo, string sku, string skuName, int quantity, decimal unitPrice, string? promotionCode, decimal promotionPercent, decimal promotionAmount)
    {
        Guard.Against(quantity <= 0, "order.qty", "Số lượng mỗi dòng phải lớn hơn 0.");
        LineNo = lineNo; Sku = sku; SkuName = skuName; Quantity = quantity; UnitPrice = unitPrice; GrossAmount = unitPrice * quantity;
        PromotionCode = promotionCode; PromotionPercent = promotionPercent; PromotionAmount = promotionAmount;
    }
    public int LineNo { get; private set; }
    public string Sku { get; private set; } = "";
    public string SkuName { get; private set; } = "";
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal GrossAmount { get; private set; }
    public string? PromotionCode { get; private set; }
    public decimal PromotionPercent { get; private set; }
    public decimal PromotionAmount { get; private set; }
    public decimal BatchDiscountAmount => _allocations.Sum(a => a.BatchDiscountAmount);
    public decimal DiscountAmount => PromotionAmount + BatchDiscountAmount;
    /// <summary>Revenue after promotion (and batch discount) – what the retailer pays and what KPI / loyalty use.</summary>
    public decimal NetAmount => GrossAmount - DiscountAmount;
    public IReadOnlyCollection<OrderAllocation> Allocations => _allocations.AsReadOnly();

    /// <summary>Attach the allocation. The batch discount stacks after the promotion: unit × qty × (1 − promo%) × batch%.</summary>
    public void Allocate(IEnumerable<OrderAllocation> allocations, IReadOnlyDictionary<string, decimal> batchDiscounts)
    {
        _allocations.Clear();
        foreach (var a in allocations)
        {
            var pct = batchDiscounts.GetValueOrDefault(a.BatchCode);
            var amount = pct <= 0 ? 0 : Math.Round(UnitPrice * a.Quantity * (1 - PromotionPercent / 100m) * pct / 100m, 0, MidpointRounding.AwayFromZero);
            a.ApplyBatchDiscount(pct, amount);
            _allocations.Add(a);
        }
        Guard.Against(_allocations.Count > 0 && _allocations.Sum(a => a.Quantity) != Quantity, "order.allocation", $"Phân bổ {Sku} không đủ số lượng.");
    }
}

public sealed class OrderStatusEntry
{
    private OrderStatusEntry() { }
    public OrderStatusEntry(int seq, OrderStatus status, DateTime atUtc, string byUserId, string byName, string? note) { Seq = seq; Status = status; AtUtc = atUtc; ByUserId = byUserId; ByName = byName; Note = note; }
    public int Seq { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string ByUserId { get; private set; } = "";
    public string ByName { get; private set; } = "";
    public string? Note { get; private set; }
}

public sealed record NewOrder(string Code, string RetailerCode, string RetailerName, string? SalesUserId, string? SalesUserName,
    string CreatedByUserId, string CreatedByName, OrderSource Source, Guid? ClientRequestId, string? DeviceCode, string? LocalCode, DateTime? CreatedOnDeviceUtc,
    PaymentMethod Payment, int CreditTermDays, DateOnly? PromisedDate, DateTime NowUtc, string? Note);

public sealed record DeliverySnapshot(string Plate, string VehicleType, string DriverCode, string DriverName, string DriverPhone);

/// <summary>
/// Order DH-000000. SalesUserId is set only when a Sales created the order – that is "closed by Sales" for ranking.
/// Status flow: Pending → Approved → Picking → Shipped → Delivering → Delivered → Completed; Pending → Rejected; Pending / Approved → Cancelled.
/// Completed = delivered and (COD / prepaid at delivery, credit when the receivable is paid).
/// </summary>
public sealed class Order : AggregateRoot<string>
{
    private readonly List<OrderLine> _lines = new();
    private readonly List<OrderStatusEntry> _history = new();
    private Order() { }

    public static Order Place(NewOrder h, IEnumerable<OrderLine> lines, string traceJson)
    {
        var o = Create(h, lines, traceJson);
        Guard.Against(o._lines.Count == 0, "order.empty", "Đơn cần ít nhất một dòng.");
        o.Status = OrderStatus.Pending; o.StockHeld = true;
        o.Log(OrderStatus.Pending, h.CreatedByUserId, h.CreatedByName, h.Source == OrderSource.Offline ? "Đồng bộ từ đơn offline" : "Tạo đơn", h.NowUtc);
        return o;
    }

    /// <summary>Offline sync result when the stock is gone (or another rule fails): the order is stored as Rejected with the reason, nothing is held.</summary>
    public static Order PlaceRejected(NewOrder h, IEnumerable<OrderLine> lines, string reason, string traceJson)
    {
        var o = Create(h, lines, traceJson);
        o.Status = OrderStatus.Rejected; o.RejectReason = reason; o.StockHeld = false;
        o.Log(OrderStatus.Rejected, "system", "Hệ thống", reason, h.NowUtc);
        return o;
    }

    private static Order Create(NewOrder h, IEnumerable<OrderLine> lines, string traceJson)
    {
        Guard.Against(h.Payment == PaymentMethod.Credit && h.CreditTermDays is not (7 or 14 or 30), "order.credit_term", "Công nợ chỉ có kỳ hạn 7, 14 hoặc 30 ngày.");
        Guard.Against(h.Payment != PaymentMethod.Credit && h.CreditTermDays != 0, "order.credit_term", "Chỉ đơn công nợ mới có kỳ hạn.");
        var o = new Order
        {
            Id = h.Code, RetailerCode = h.RetailerCode, RetailerName = h.RetailerName, SalesUserId = h.SalesUserId, SalesUserName = h.SalesUserName,
            CreatedByUserId = h.CreatedByUserId, CreatedByName = h.CreatedByName, Source = h.Source, ClientRequestId = h.ClientRequestId, DeviceCode = h.DeviceCode,
            LocalCode = h.LocalCode, CreatedOnDeviceUtc = h.CreatedOnDeviceUtc, Payment = h.Payment, CreditTermDays = h.CreditTermDays,
            PromisedDate = h.PromisedDate, CreatedAtUtc = h.NowUtc, Note = h.Note, AllocationTraceJson = traceJson
        };
        o._lines.AddRange(lines);
        return o;
    }

    public string RetailerCode { get; private set; } = "";
    public string RetailerName { get; private set; } = "";
    public string? SalesUserId { get; private set; }
    public string? SalesUserName { get; private set; }
    public string CreatedByUserId { get; private set; } = "";
    public string CreatedByName { get; private set; } = "";
    public OrderSource Source { get; private set; }
    public Guid? ClientRequestId { get; private set; }
    public string? DeviceCode { get; private set; }
    public string? LocalCode { get; private set; }
    public DateTime? CreatedOnDeviceUtc { get; private set; }
    public PaymentMethod Payment { get; private set; }
    public int CreditTermDays { get; private set; }
    public DateOnly? PromisedDate { get; private set; }
    public string? Note { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? RejectReason { get; private set; }
    public bool StockHeld { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateOnly? IssuedOn { get; private set; }
    public DateOnly? DeliveredOn { get; private set; }
    public DateOnly? CompletedOn { get; private set; }
    /// <summary>JSON of the per-line B1–B4 trace (see AllocationEngine) for the Portal explanation panel.</summary>
    public string AllocationTraceJson { get; private set; } = "{}";

    // delivery snapshot (filled when the order goes to "Đang giao")
    public string? DeliveryPlate { get; private set; }
    public string? VehicleType { get; private set; }
    public string? DriverCode { get; private set; }
    public string? DriverName { get; private set; }
    public string? DriverPhone { get; private set; }
    public decimal? EstimatedDeliveryCost { get; private set; }
    public DateTime? DispatchedAtUtc { get; private set; }
    /// <summary>Secret inside the QR printed on the delivery note; the retailer scans it to confirm receipt.</summary>
    public string? ReceiptToken { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();
    public IReadOnlyCollection<OrderStatusEntry> History => _history.AsReadOnly();

    public decimal GrossAmount => _lines.Sum(l => l.GrossAmount);
    public decimal PromotionAmount => _lines.Sum(l => l.PromotionAmount);
    public decimal BatchDiscountAmount => _lines.Sum(l => l.BatchDiscountAmount);
    public decimal DiscountAmount => PromotionAmount + BatchDiscountAmount;
    public decimal NetAmount => GrossAmount - DiscountAmount;
    public int TotalCases => _lines.Sum(l => l.Quantity);
    public bool ClosedBySales => SalesUserId is not null;
    public IEnumerable<string> Warehouses => _lines.SelectMany(l => l.Allocations).Select(a => a.WarehouseCode).Distinct().OrderBy(x => x, StringComparer.Ordinal);

    public void Approve(string byId, string byName, DateTime now) { Move(OrderStatus.Approved, byId, byName, null, now); }
    public void Reject(string byId, string byName, string reason, DateTime now)
    {
        Guard.Against(string.IsNullOrWhiteSpace(reason), "order.reason", "Cần nhập lý do từ chối.");
        Move(OrderStatus.Rejected, byId, byName, reason, now); RejectReason = reason;
    }
    public void Cancel(string byId, string byName, DateTime now) { Move(OrderStatus.Cancelled, byId, byName, null, now); }
    public void StartPicking(string byId, string byName, DateTime now) { if (Status == OrderStatus.Approved) Move(OrderStatus.Picking, byId, byName, null, now); }

    /// <summary>All picking lists done → goods have left the warehouse.</summary>
    public void MarkIssued(string byId, string byName, DateTime now, DateOnly today) { Move(OrderStatus.Shipped, byId, byName, null, now); IssuedOn = today; StockHeld = false; }

    public void Dispatch(DeliverySnapshot d, decimal estimatedCost, string receiptToken, string byId, string byName, DateTime now)
    {
        Move(OrderStatus.Delivering, byId, byName, $"Xe {d.Plate} · {d.DriverName}", now);
        DeliveryPlate = d.Plate; VehicleType = d.VehicleType; DriverCode = d.DriverCode; DriverName = d.DriverName; DriverPhone = d.DriverPhone;
        EstimatedDeliveryCost = estimatedCost; DispatchedAtUtc = now; ReceiptToken = receiptToken;
    }

    /// <summary>Retailer scans the QR. COD / prepaid orders complete immediately; credit orders complete when Finance reports the payment.</summary>
    public void ConfirmReceipt(string qrToken, string byId, string byName, DateTime now, DateOnly today)
    {
        Guard.Against(Status != OrderStatus.Delivering, "order.transition", $"Đơn {Id} chưa ở trạng thái đang giao.");
        Guard.Against(ReceiptToken is null || !string.Equals(ReceiptToken, qrToken, StringComparison.Ordinal), "order.qr", "Mã QR không khớp với đơn hàng.");
        Move(OrderStatus.Delivered, byId, byName, "Retailer quét QR xác nhận nhận hàng", now); DeliveredOn = today;
        if (Payment != PaymentMethod.Credit) Complete(byId, byName, now, today);
    }

    public void Complete(string byId, string byName, DateTime now, DateOnly today)
    {
        Move(OrderStatus.Completed, byId, byName, Payment == PaymentMethod.Credit ? "Đã thu công nợ" : "Hoàn tất", now); CompletedOn = today;
    }

    private void Move(OrderStatus to, string byId, string byName, string? note, DateTime now)
    {
        Guard.Against(!CanMove(Status, to), "order.transition", $"Không thể chuyển đơn {Id} từ {Status} sang {to}.");
        Status = to; Log(to, byId, byName, note, now);
        if (to is OrderStatus.Rejected or OrderStatus.Cancelled) { StockHeld = false; }
    }
    private void Log(OrderStatus s, string byId, string byName, string? note, DateTime now) => _history.Add(new OrderStatusEntry(_history.Count + 1, s, now, byId, byName, note));

    public static bool CanMove(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Pending, OrderStatus.Approved or OrderStatus.Rejected or OrderStatus.Cancelled) => true,
        (OrderStatus.Approved, OrderStatus.Picking or OrderStatus.Cancelled) => true,
        (OrderStatus.Picking, OrderStatus.Shipped) => true,
        (OrderStatus.Shipped, OrderStatus.Delivering) => true,
        (OrderStatus.Delivering, OrderStatus.Delivered) => true,
        (OrderStatus.Delivered, OrderStatus.Completed) => true,
        _ => false
    };
}
