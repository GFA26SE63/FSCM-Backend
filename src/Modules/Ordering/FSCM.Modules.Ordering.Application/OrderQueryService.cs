using System.Text.Json;
using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Ordering.Contracts;
using FSCM.Modules.Ordering.Domain.Orders;
using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Ordering.Application;

/// <summary>Read side: order list (scoped by role), detail with delivery cost, allocation trace for the "Giải thích phân bổ" panel.</summary>
public sealed class OrderQueryService(IOrderRepository orders, IPickingRepository pickings, ICurrentUser user)
{
    public async Task<PagedResult<OrderSummaryDto>> SearchAsync(PageRequest page, string? status, string? retailerCode, CancellationToken ct)
    {
        OrderStatus? st = Enum.TryParse<OrderStatus>(status, true, out var s) ? s : null;
        var filter = new OrderFilter(st, user.Role == Role.Retailer ? user.RetailerCode : retailerCode, user.Role == Role.Sales ? user.UserId : null, page.Search);
        return (await orders.SearchAsync(filter, ct)).Select(o => new OrderSummaryDto(o.Id, o.RetailerCode, o.RetailerName, o.Status.ToString(), o.Payment.ToString(), o.CreditTermDays, o.NetAmount,
            o.CreatedAtUtc, o.SalesUserName, o.Source.ToString(), o.Lines.Count, o.TotalCases)).Page(page);
    }

    public async Task<OrderDetailDto> GetAsync(string code, CancellationToken ct)
    {
        var o = await LoadVisible(code, ct);
        var lists = (await pickings.ByOrderAsync(code, ct)).Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var staff = user.Role is Role.Admin or Role.Operator;
        var parts = o.Lines.SelectMany(l => l.Allocations).GroupBy(a => a.WarehouseCode).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new DeliveryCostPart(g.Key, g.First().DistanceKm, g.First().TripCost)).ToList();
        return new OrderDetailDto(o.Id, o.RetailerCode, o.RetailerName, o.Status.ToString(), o.Payment.ToString(), o.CreditTermDays, o.Source.ToString(), o.LocalCode,
            o.SalesUserId, o.SalesUserName, o.CreatedByName, o.CreatedAtUtc, o.PromisedDate, o.Note, o.RejectReason,
            o.GrossAmount, o.PromotionAmount, o.BatchDiscountAmount, o.NetAmount,
            o.Lines.Select(l => new OrderLineDto(l.LineNo, l.Sku, l.SkuName, l.Quantity, l.UnitPrice, l.GrossAmount, l.PromotionCode, l.PromotionPercent, l.PromotionAmount, l.BatchDiscountAmount, l.NetAmount,
                l.Allocations.Select(a => new AllocationDto(a.BatchCode, a.WarehouseCode, a.Quantity, a.DistanceKm, a.TransitDays, a.Eta, a.RemainingDays, a.Score, a.TripCost, a.BatchDiscountPercent, a.BatchDiscountAmount)).ToList())).ToList(),
            o.History.Select(h => new StatusEntryDto(h.Seq, h.Status.ToString(), h.AtUtc, h.ByName, h.Note)).ToList(),
            new DeliveryDto(o.DeliveryPlate, o.VehicleType, o.DriverCode, o.DriverName, o.DriverPhone, o.DispatchedAtUtc),
            o.EstimatedDeliveryCost ?? parts.Sum(p => p.Cost), parts, lists,
            staff && o.ReceiptToken is not null && o.Status == OrderStatus.Delivering ? $"FSCM|{o.Id}|{o.ReceiptToken}" : null);
    }

    /// <summary>JSON with B1..B4 tables per line (see AllocationEngine) – rendered by the Portal order detail.</summary>
    public async Task<JsonElement> TraceAsync(string code, CancellationToken ct)
    {
        var o = await LoadVisible(code, ct);
        return JsonDocument.Parse(o.AllocationTraceJson).RootElement.Clone();
    }

    private async Task<Order> LoadVisible(string code, CancellationToken ct)
    {
        var o = await orders.GetAsync(code, ct) ?? throw new BusinessRuleException("order.not_found", $"Không có đơn {code}.");
        if ((user.Role == Role.Sales && o.SalesUserId != user.UserId) || (user.Role == Role.Retailer && o.RetailerCode != user.RetailerCode))
            throw new UnauthorizedAccessException($"Bạn không có quyền xem đơn {code}.");
        return o;
    }
}
