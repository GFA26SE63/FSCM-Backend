using System.Text.Json;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Reports;

public sealed class OperationsReportStore(AppDbContext db) : IOperationsReportStore
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    public Task<OperationsReportResult> ReadAsync(string kind, OperationsReportQuery query, CancellationToken ct) =>
        ReadAsync(kind, query, null, null, ct);

    public async Task<OperationsReportResult> ReadAsync(string kind, OperationsReportQuery q,
        Guid? actorUserId, string? actorRole, CancellationToken ct)
    {
        var start = new DateTimeOffset(q.FromDate.ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();
        var end = new DateTimeOffset(q.ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Vietnam).DateTime);
        var d7 = today.AddDays(-7);
        var d30 = today.AddDays(-30);

        IQueryable<OperationsReportRow> source = kind switch
        {
            "redemptions" => Redemptions(),
            "loyalty" => Loyalty(),
            "promotions" => Promotions(),
            "disposals" => Disposals(),
            "claims" => Claims(),
            "receivables" => Receivables(today, d7, d30),
            _ => throw ReportOperationException.Validation("Unknown report kind.")
        };

        if (actorRole == SystemRoles.SalesManager && actorUserId.HasValue)
        {
            var groupIds = ReportingScopeQueries.ManagedGroups(db, actorUserId.Value).Select(g => g.Id);
            if (kind is "redemptions" or "loyalty")
            {
                source = source.Where(r => db.RetailerSalesAssignmentRecords.Any(a =>
                    a.RetailerId == r.RetailerId && a.IsActive && a.UnassignedAt == null && groupIds.Contains(a.SalesGroupId)));
            }
            else if (kind is "promotions" or "claims" or "receivables")
            {
                source = source.Where(r => r.OrderId.HasValue && db.SalesOrderRecords.Any(o =>
                    o.Id == r.OrderId.Value && groupIds.Contains(o.SalesGroupId)));
            }
        }
        else if (actorRole == SystemRoles.WarehouseKeeper && actorUserId.HasValue && kind == "disposals")
        {
            var warehouseIds = ReportingScopeQueries.ManagedWarehouses(db, actorUserId.Value).Select(w => w.Id);
            source = source.Where(r => warehouseIds.Contains(r.RetailerId));
        }

        if (q.RegionId.HasValue) source = source.Where(r => r.RegionId == q.RegionId);
        if (!string.IsNullOrWhiteSpace(q.Retailer))
        {
            var term = q.Retailer.Trim().ToLower();
            source = source.Where(r => r.RetailerCode.ToLower().Contains(term) || r.RetailerName.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(q.Program))
        {
            var term = q.Program.Trim().ToLower();
            source = kind == "claims"
                ? source.Where(r => r.ItemCode.ToLower().Contains(term)
                    || (r.OrderCode != null && r.OrderCode.ToLower().Contains(term)))
                : source.Where(r => r.ProgramCode.ToLower().Contains(term)
                    || r.ProgramName.ToLower().Contains(term)
                    || r.ItemCode.ToLower().Contains(term)
                    || r.ItemName.ToLower().Contains(term));
        }
        // Balance totals use the entire filtered ledger, regardless of transaction-type filter.
        var ledger = source.Where(r => r.Status != "PendingEarn" && r.Status != "PendingEarnCancelled" && r.Status != "Pending");
        source = source.Where(r => r.At >= start && r.At < end);
        if (!string.IsNullOrWhiteSpace(q.Status)) source = source.Where(r => r.Status == q.Status);

        var count = await source.CountAsync(ct);
        var summary = new List<OperationsReportMetric>
        {
            new("Số bản ghi", count),
            new(kind == "disposals" ? "Số kho" : "Retailer", await source.Select(r => r.RetailerId).Distinct().CountAsync(ct))
        };
        var notes = new List<string>();
        if (kind == "redemptions")
        {
            summary.Add(new("Hoàn tất", await source.CountAsync(r => r.Status == "Completed", ct)));
            summary.Add(new("Đang chờ xử lý", await source.CountAsync(r => r.Status == "Pending" || r.Status == "Approved" || r.Status == "Delivered", ct)));
            summary.Add(new("Điểm đã đổi hoàn tất", await source.Where(r => r.Status == "Completed").SumAsync(r => (decimal?)r.Points, ct) ?? 0));
            summary.Add(new("Quà đã giao (có số lượng lưu)", await source.Where(r => r.Status == "Completed" || r.Status == "Delivered").SumAsync(r => r.Quantity, ct) ?? 0));
            notes.Add("Kỳ báo cáo theo ngày tạo yêu cầu; trạng thái là hiện tại. Chỉ Completed được tính là đổi hoàn tất.");
            notes.Add("Yêu cầu cũ chưa lưu số lượng/người xử lý hiển thị —; không suy ra số lượng từ giá điểm hiện tại của quà.");
        }
        else if (kind == "loyalty")
        {
            summary.Add(new("Biến động trước kỳ", await ledger.Where(r => r.At < start).SumAsync(r => (decimal?)r.Points, ct) ?? 0));
            var moving = source.Where(r => r.Status != "PendingEarn" && r.Status != "PendingEarnCancelled" && r.Status != "Pending");
            summary.Add(new("Điểm khả dụng tăng trong bộ lọc", await moving.Where(r => r.Points > 0).SumAsync(r => (decimal?)r.Points, ct) ?? 0));
            summary.Add(new("Điểm khả dụng giảm trong bộ lọc", -(await moving.Where(r => r.Points < 0).SumAsync(r => (decimal?)r.Points, ct) ?? 0)));
            summary.Add(new("Biến động lũy kế hết kỳ", await ledger.Where(r => r.At < end).SumAsync(r => (decimal?)r.Points, ct) ?? 0));
            summary.Add(new("Điểm tích trong bộ lọc", await source.Where(r => r.Status == "Earned").SumAsync(r => (decimal?)r.Points, ct) ?? 0));
            summary.Add(new("Điểm hết hạn trong bộ lọc", -(await source.Where(r => r.Status == "Expired").SumAsync(r => (decimal?)r.Points, ct) ?? 0)));
            summary.Add(new("Điểm hoàn do từ chối đổi quà", await source.Where(r => r.Status == "RedemptionRejected").SumAsync(r => (decimal?)r.Points, ct) ?? 0));
            var retailerIds = source.Select(r => r.RetailerId).Distinct();
            var currentCards = db.RetailerLoyaltyRecords.AsNoTracking().Where(c => retailerIds.Contains(c.RetailerId));
            summary.Add(new("Điểm khả dụng hiện tại · toàn thẻ", await currentCards.SumAsync(c => (decimal?)c.CurrentPoints, ct) ?? 0));
            summary.Add(new("Điểm đang giữ hiện tại · toàn thẻ", await currentCards.SumAsync(c => (decimal?)c.HeldPoints, ct) ?? 0));
            notes.Add("Điểm là biến động sổ giao dịch đúng loại nghiệp vụ. RedemptionHeld trừ điểm khả dụng; RedemptionCompleted có thể bằng 0 vì điểm đã giữ trước đó.");
            notes.Add("Lũy kế là tổng biến động sổ, không thay thế số dư khả dụng/đang giữ hiện tại trên thẻ loyalty. Khi lọc chương trình, BalanceAfter vẫn là số dư toàn thẻ sau giao dịch.");
            notes.Add("Hai chỉ số hiện tại lấy từ toàn thẻ của các retailer có giao dịch trong bộ lọc, bao gồm mọi chương trình; không phải số dư tại ngày cuối kỳ.");
        }
        else if (kind == "promotions")
        {
            summary.Add(new("Đơn áp dụng", await source.Select(r => r.OrderId).Distinct().CountAsync(ct)));
            summary.Add(new("Tiền giảm theo giá chốt", await source.SumAsync(r => (decimal?)r.DiscountAmount, ct) ?? 0));
            summary.Add(new("Giảm do chiến dịch", await source.SumAsync(r => (decimal?)r.CampaignDiscountAmount, ct) ?? 0));
            summary.Add(new("Giảm theo HSD", await source.SumAsync(r => (decimal?)r.ShelfLifeDiscountAmount, ct) ?? 0));
            summary.Add(new("Tiền giảm · đơn Completed", await source.Where(r => r.Status == "Completed").SumAsync(r => (decimal?)r.DiscountAmount, ct) ?? 0));
            notes.Add("Theo ngày chấp thuận báo giá, giá chốt từng SKU/batch; chỉ lấy phiên bản báo giá đã chấp thuận mới nhất của mỗi đơn. Không tính lại từ cấu hình khuyến mãi hiện tại.");
            notes.Add("Giảm chiến dịch và giảm HSD được tách theo thành phần giá đã chốt. Nếu đơn cũ không lưu mã chiến dịch thì hiển thị Chưa lưu mã; không suy ra chương trình từ cấu hình hiện tại.");
            notes.Add("Tiền giảm là giá chốt trước điều chỉnh trả hàng/khiếu nại, không phải doanh thu ghi nhận. Đơn hủy vẫn hiển thị theo trạng thái để đối soát; lọc Completed để xem đơn hoàn tất.");
        }
        else if (kind == "disposals")
        {
            summary.Add(new("Số phiếu hủy", await source.Select(r => r.ProgramName).Distinct().CountAsync(ct)));
            summary.Add(new("Tổng thùng hủy", await source.SumAsync(r => r.Quantity, ct) ?? 0));
            summary.Add(new("Giá trị tổn thất ước tính (đ)", await source.SumAsync(r => (decimal?)r.GrossAmount, ct) ?? 0));
            notes.Add("Báo cáo liệt kê chi tiết các lô hàng hết hạn đã lập biên bản tiêu hủy trong kỳ thời gian lọc.");
            notes.Add("Giá trị tổn thất được ước tính theo đơn giá niêm yết hiệu lực tại thời điểm lập phiếu tiêu hủy.");
        }
        else if (kind == "claims")
        {
            summary.Add(new("Đã đóng / xử lý", await source.CountAsync(r => r.CompletedAt != null, ct)));
            summary.Add(new("Đang chờ xử lý", await source.CountAsync(r => r.CompletedAt == null && r.Status != "Rejected" && r.Status != "Cancelled", ct)));
            summary.Add(new("Tổng cấn trừ công nợ (đ)", await source.SumAsync(r => (decimal?)r.CampaignDiscountAmount, ct) ?? 0));
            summary.Add(new("Tổng hoàn tiền mặt (đ)", await source.SumAsync(r => (decimal?)r.ShelfLifeDiscountAmount, ct) ?? 0));
            notes.Add("Thống kê toàn bộ khiếu nại, yêu cầu trả hàng và thu hồi hàng hỏng theo ngày tiếp nhận.");
        }
        else if (kind == "receivables")
        {
            summary.Add(new("Tổng phải thu (đ)", await source.SumAsync(r => (decimal?)r.NetAmount, ct) ?? 0));
            summary.Add(new("Chưa đến hạn (đ)", await source.Where(r => r.Status == "Current").SumAsync(r => (decimal?)r.NetAmount, ct) ?? 0));
            summary.Add(new("Quá hạn 1–7 ngày (đ)", await source.Where(r => r.Status == "Overdue_1_7").SumAsync(r => (decimal?)r.NetAmount, ct) ?? 0));
            summary.Add(new("Quá hạn > 7 ngày (đ)", await source.Where(r => r.Status == "Overdue_8_30" || r.Status == "Overdue_31_Plus").SumAsync(r => (decimal?)r.NetAmount, ct) ?? 0));
            notes.Add("Danh sách các đơn công nợ (Credit) đã giao hàng và vẫn còn số dư phải thu tại thời điểm hiện tại.");
        }

        List<PromotionProgramSummaryRow>? byProgram = null;
        if (kind == "promotions")
        {
            var promoData = await source
                .Select(r => new
                {
                    r.ProgramCode, r.ProgramName, r.OrderId, r.Quantity, r.GrossAmount, r.DiscountAmount,
                    r.CampaignDiscountAmount, r.ShelfLifeDiscountAmount, r.NetAmount, r.Status
                })
                .ToListAsync(ct);

            byProgram = promoData
                .GroupBy(r => new { r.ProgramCode, r.ProgramName })
                .Select(g => new PromotionProgramSummaryRow(
                    g.Key.ProgramCode, g.Key.ProgramName,
                    g.Select(r => r.OrderId).Where(id => id.HasValue).Distinct().Count(),
                    g.Sum(r => r.Quantity ?? 0), g.Sum(r => r.GrossAmount), g.Sum(r => r.DiscountAmount),
                    g.Sum(r => r.CampaignDiscountAmount), g.Sum(r => r.ShelfLifeDiscountAmount),
                    g.Sum(r => r.NetAmount), g.Where(r => r.Status == "Completed").Sum(r => r.DiscountAmount)
                ))
                .OrderByDescending(p => p.DiscountAmount)
                .ToList();
        }
        var items = await source.OrderByDescending(r => r.At).ThenBy(r => r.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToArrayAsync(ct);

        if (kind == "disposals")
        {
            var lineIds = items.Select(x => x.Id).ToArray();
            var expByLineId = lineIds.Length == 0
                ? new Dictionary<Guid, DateOnly>()
                : await (from l in db.WriteOffItemRecords.AsNoTracking()
                         join b in db.BatchRecords.AsNoTracking() on l.BatchId equals b.Id
                         where lineIds.Contains(l.Id)
                         select new { l.Id, b.ExpirationDate })
                    .ToDictionaryAsync(x => x.Id, x => x.ExpirationDate, ct);
            items = items.Select(item =>
            {
                var method = ExtractDisposalMethod(item.Note);
                var expNote = expByLineId.TryGetValue(item.Id, out var exp)
                    ? $"HSD: {exp:dd/MM/yyyy} · Kho {item.RetailerCode}"
                    : $"Kho {item.RetailerCode}";
                return new OperationsReportRow
                {
                    Id = item.Id, At = item.At, RetailerId = item.RetailerId, RetailerCode = item.RetailerCode,
                    RetailerName = item.RetailerName, RegionId = item.RegionId, RegionName = item.RegionName,
                    ProgramCode = item.ProgramCode, ProgramName = item.ProgramName, ItemCode = item.ItemCode,
                    ItemName = item.ItemName, Status = method, Quantity = item.Quantity, GrossAmount = item.GrossAmount,
                    NetAmount = item.NetAmount, CreatedByUserId = item.CreatedByUserId, CreatedByName = item.CreatedByName,
                    Note = expNote
                };
            }).ToArray();
        }
        else if (kind == "receivables" && items.Length > 0)
        {
            var paymentIds = items.Select(x => x.Id).ToArray();
            var dates = await db.PaymentRecords.AsNoTracking()
                .Where(p => paymentIds.Contains(p.Id))
                .Select(p => new { p.Id, p.DueDate, p.DeliveredDate })
                .ToDictionaryAsync(x => x.Id, ct);
            items = items.Select(item =>
            {
                dates.TryGetValue(item.Id, out var d);
                return new OperationsReportRow
                {
                    Id = item.Id, At = item.At, RetailerId = item.RetailerId, RetailerCode = item.RetailerCode,
                    RetailerName = item.RetailerName, RegionId = item.RegionId, RegionName = item.RegionName,
                    ProgramCode = item.ProgramCode, ProgramName = item.ProgramName, ItemCode = item.ItemCode,
                    ItemName = d?.DueDate.HasValue == true ? $"Hạn thu: {d.DueDate.Value:dd/MM/yyyy}" : "Chưa có hạn thu",
                    Status = item.Status, GrossAmount = item.GrossAmount, DiscountAmount = item.DiscountAmount,
                    NetAmount = item.NetAmount, OrderId = item.OrderId, OrderCode = item.OrderCode,
                    DeliveredAt = d?.DeliveredDate.HasValue == true
                        ? new DateTimeOffset(d.DeliveredDate.Value.ToDateTime(TimeOnly.MinValue), Vietnam)
                        : null,
                    Note = item.Note
                };
            }).ToArray();
        }

        OperationsReportPointsTotal? pointsTotal = null;
        if (kind == "loyalty")
        {
            var received = await source.Where(r => r.Points > 0).SumAsync(r => (decimal?)r.Points, ct) ?? 0;
            var used = -(await source.Where(r => r.Points < 0).SumAsync(r => (decimal?)r.Points, ct) ?? 0);
            pointsTotal = new(received, used, received - used);
        }
        return new(items, count, q.Page, q.PageSize, summary, notes, byProgram, pointsTotal);
    }

    private static string ExtractDisposalMethod(string? rawReason)
    {
        if (string.IsNullOrWhiteSpace(rawReason)) return "Tiêu hủy tại kho";
        try
        {
            using var doc = JsonDocument.Parse(rawReason);
            if (doc.RootElement.TryGetProperty("Method", out var m) && m.GetString() is { Length: > 0 } str)
                return str;
        }
        catch { /* fallback */ }
        return "Tiêu hủy tại kho";
    }

    private IQueryable<OperationsReportRow> Redemptions() =>
        from x in db.RewardRedemptionRecords.AsNoTracking()
        join r in db.RetailerRecords on x.RetailerId equals r.Id
        join gift in db.LoyaltyRewardRecords on x.LoyaltyRewardId equals gift.Id
        join p in db.LoyaltyProgramRecords on gift.LoyaltyProgramId equals p.Id
        select new OperationsReportRow
        {
            Id = x.Id, At = x.CreatedAt, RetailerId = r.Id, RetailerCode = r.Code, RetailerName = r.Name,
            RegionId = r.RegionId, RegionName = db.RegionRecords.Where(z => z.Id == r.RegionId).Select(z => z.Name).FirstOrDefault() ?? "",
            ProgramCode = p.Code, ProgramName = p.Name, ItemCode = gift.Code,
            ItemName = gift.Name, Quantity = x.Quantity, Points = x.RedeemedPoints, Status = x.Status,
            OrderId = x.SalesOrderId, TransactionId = x.LoyaltyTransactionId, CreatedByUserId = x.RedeemedByUserId,
            OrderCode = db.SalesOrderRecords.Where(o => o.Id == x.SalesOrderId).Select(o => o.OrderCode).FirstOrDefault(),
            CreatedByName = db.UserAccountRecords.Where(u => u.Id == x.RedeemedByUserId).Select(u => u.FullName).FirstOrDefault(),
            ApprovedByName = db.UserAccountRecords.Where(u => u.Id == x.ApprovedByUserId).Select(u => u.FullName).FirstOrDefault(),
            DeliveredByName = db.UserAccountRecords.Where(u => u.Id == x.DeliveredByUserId).Select(u => u.FullName).FirstOrDefault(),
            ApprovedByUserId = x.ApprovedByUserId, ApprovedAt = x.ApprovedAt,
            DeliveredByUserId = x.DeliveredByUserId, DeliveredAt = x.DeliveredAt,
            CompletedAt = x.FulfilledAt, CancelledAt = x.CancelledAt, Note = x.Note
        };

    private IQueryable<OperationsReportRow> Loyalty() =>
        from x in db.LoyaltyTransactionRecords.AsNoTracking()
        join card in db.RetailerLoyaltyRecords on x.RetailerLoyaltyId equals card.Id
        join r in db.RetailerRecords on card.RetailerId equals r.Id
        join p in db.LoyaltyProgramRecords on x.LoyaltyProgramId equals p.Id
        select new OperationsReportRow
        {
            Id = x.Id, At = x.CreatedAt, RetailerId = r.Id, RetailerCode = r.Code, RetailerName = r.Name,
            RegionId = r.RegionId, RegionName = db.RegionRecords.Where(z => z.Id == r.RegionId).Select(z => z.Name).FirstOrDefault() ?? "",
            ProgramCode = p.Code, ProgramName = p.Name, ItemCode = "", ItemName = "", Status = x.TransactionType,
            Points = x.Points, BalanceAfter = x.BalanceAfter, OrderId = x.SalesOrderId,
            TransactionId = x.Id, CreatedByUserId = x.CreatedByUserId, Note = x.Note,
            OrderCode = db.SalesOrderRecords.Where(o => o.Id == x.SalesOrderId).Select(o => o.OrderCode).FirstOrDefault(),
            CreatedByName = db.UserAccountRecords.Where(u => u.Id == x.CreatedByUserId).Select(u => u.FullName).FirstOrDefault()
        };

    private IQueryable<OperationsReportRow> Promotions() =>
        from quote in db.SalesOrderQuoteRecords.AsNoTracking()
        where quote.AcceptedAt != null && !db.SalesOrderQuoteRecords.Any(other =>
            other.SalesOrderId == quote.SalesOrderId && other.AcceptedAt != null && other.RevisionNumber > quote.RevisionNumber)
        join price in db.AllocationPlanPriceRecords on quote.AllocationRunId equals price.AllocationRunId
        join frozenLine in db.AllocationRunLineRecords on price.AllocationRunLineId equals frozenLine.Id
        join line in db.SalesOrderLineRecords on frozenLine.SalesOrderLineId equals line.Id
        where line.SalesOrderId == quote.SalesOrderId && price.ListUnitPrice > price.FinalNetUnitPrice
        join order in db.SalesOrderRecords on quote.SalesOrderId equals order.Id
        join r in db.RetailerRecords on order.RetailerId equals r.Id
        join batch in db.BatchRecords on price.BatchId equals batch.Id
        let campaignCode = db.OrderPromotionRecords.Where(p => p.SalesOrderLineId == line.Id && p.AppliedAt <= quote.AcceptedAt)
            .OrderByDescending(p => p.AppliedAt).Select(p => p.PromotionCodeSnapshot).FirstOrDefault()
        let campaignName = db.OrderPromotionRecords.Where(p => p.SalesOrderLineId == line.Id && p.AppliedAt <= quote.AcceptedAt)
            .OrderByDescending(p => p.AppliedAt).Select(p => p.PromotionNameSnapshot).FirstOrDefault()
        select new OperationsReportRow
        {
            Id = price.Id, At = quote.AcceptedAt!.Value, RetailerId = r.Id,
            RetailerCode = order.RetailerCodeSnapshot, RetailerName = order.RetailerNameSnapshot, RegionId = r.RegionId,
            RegionName = db.RegionRecords.Where(z => z.Id == r.RegionId).Select(z => z.Name).FirstOrDefault() ?? "",
            ProgramCode = price.BaseNetUnitPrice < price.ListUnitPrice
                ? (campaignCode ?? "Chưa lưu mã") + (price.ShelfLifeDiscountPerUnit > 0 ? " + HSD-V" + price.DiscountTableVersion : "")
                : "HSD-V" + price.DiscountTableVersion,
            ProgramName = price.BaseNetUnitPrice < price.ListUnitPrice
                ? (campaignName ?? "Khuyến mãi chưa lưu tên chiến dịch") + (price.ShelfLifeDiscountPerUnit > 0 ? " + giảm theo HSD" : "")
                : "Giảm giá theo HSD",
            ItemCode = line.SkuSnapshot, ItemName = line.ProductNameSnapshot,
            Quantity = price.Quantity, GrossAmount = price.ListUnitPrice * price.Quantity,
            DiscountAmount = price.ListUnitPrice * price.Quantity - price.Amount,
            CampaignDiscountAmount = (price.ListUnitPrice - price.BaseNetUnitPrice) * price.Quantity,
            ShelfLifeDiscountAmount = price.ShelfLifeDiscountPerUnit * price.Quantity,
            NetAmount = price.Amount, OrderId = order.Id, OrderCode = order.OrderCode, Status = order.Status,
            Note = "Batch " + batch.BatchCode + " · " + price.Explanation
        };

    private IQueryable<OperationsReportRow> Disposals() =>
        from line in db.WriteOffItemRecords.AsNoTracking()
        join doc in db.WriteOffRecords.AsNoTracking() on line.WriteOffId equals doc.Id
        join wh in db.WarehouseRecords.AsNoTracking() on doc.WarehouseId equals wh.Id
        join batch in db.BatchRecords.AsNoTracking() on line.BatchId equals batch.Id
        join product in db.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
        join user in db.UserAccountRecords.AsNoTracking() on doc.CreatedByUserId equals user.Id
        let unitPrice = db.ProductPriceRecords
            .Where(pr => pr.ProductId == product.Id && pr.EffectiveFrom <= doc.CreatedAt && (pr.EffectiveTo == null || pr.EffectiveTo > doc.CreatedAt))
            .OrderByDescending(pr => pr.EffectiveFrom)
            .Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault()
            ?? db.ProductPriceRecords.Where(pr => pr.ProductId == product.Id && pr.IsActive)
                .OrderByDescending(pr => pr.EffectiveFrom)
                .Select(pr => (decimal?)pr.UnitPrice).FirstOrDefault()
            ?? 0m
        select new OperationsReportRow
        {
            Id = line.Id, At = doc.CreatedAt, RetailerId = wh.Id, RetailerCode = wh.Code, RetailerName = wh.Name,
            RegionId = wh.RegionId, RegionName = db.RegionRecords.Where(z => z.Id == wh.RegionId).Select(z => z.Name).FirstOrDefault() ?? "",
            ProgramCode = batch.BatchCode, ProgramName = doc.WriteOffCode,
            ItemCode = product.Sku, ItemName = product.Name, Status = "Completed",
            Quantity = line.Quantity, GrossAmount = line.Quantity * unitPrice, NetAmount = line.Quantity * unitPrice,
            CreatedByUserId = doc.CreatedByUserId, CreatedByName = user.FullName,
            Note = doc.Reason
        };

    private IQueryable<OperationsReportRow> Claims() =>
        from d in db.ReceiptDiscrepancyRecords.AsNoTracking()
        join o in db.SalesOrderRecords.AsNoTracking() on d.SalesOrderId equals o.Id
        join r in db.RetailerRecords.AsNoTracking() on o.RetailerId equals r.Id
        select new OperationsReportRow
        {
            Id = d.Id, At = d.ReportedAt, RetailerId = r.Id,
            RetailerCode = o.RetailerCodeSnapshot, RetailerName = o.RetailerNameSnapshot,
            RegionId = r.RegionId, RegionName = db.RegionRecords.Where(z => z.Id == r.RegionId).Select(z => z.Name).FirstOrDefault() ?? "",
            ProgramCode = d.RequestType, ProgramName = d.ApprovedSettlementMethod ?? d.SettlementMethod,
            ItemCode = "KN-" + d.Id.ToString().Substring(0, 8).ToUpper(),
            ItemName = d.ResolutionType ?? d.RequestType,
            Status = d.Status,
            Quantity = d.ApprovedQuantity ?? d.RequestedQuantity,
            GrossAmount = d.DebtOffsetAmount + d.RefundAmount,
            CampaignDiscountAmount = d.DebtOffsetAmount,
            ShelfLifeDiscountAmount = d.RefundAmount,
            NetAmount = d.RefundAmount,
            OrderId = o.Id, OrderCode = o.OrderCode,
            ApprovedByUserId = d.ReviewedByUserId,
            ApprovedByName = db.UserAccountRecords.Where(u => u.Id == d.ReviewedByUserId).Select(u => u.FullName).FirstOrDefault(),
            ApprovedAt = d.ReviewedAt, CompletedAt = d.ResolvedAt, Note = d.Note
        };

    private IQueryable<OperationsReportRow> Receivables(DateOnly today, DateOnly d7, DateOnly d30) =>
        from p in db.PaymentRecords.AsNoTracking()
        where p.Method == "Credit" && p.Status != "Cancelled" && p.DeliveredDate != null && p.AdjustedAmount > p.ConfirmedAmount
        join o in db.SalesOrderRecords.AsNoTracking() on p.SalesOrderId equals o.Id
        join r in db.RetailerRecords.AsNoTracking() on o.RetailerId equals r.Id
        join g in db.SalesGroupRecords.AsNoTracking() on o.SalesGroupId equals g.Id
        select new OperationsReportRow
        {
            Id = p.Id, At = o.ServerReceivedAt, RetailerId = r.Id,
            RetailerCode = o.RetailerCodeSnapshot, RetailerName = o.RetailerNameSnapshot,
            RegionId = r.RegionId, RegionName = db.RegionRecords.Where(z => z.Id == r.RegionId).Select(z => z.Name).FirstOrDefault() ?? "",
            ProgramCode = g.Code, ProgramName = g.Name,
            ItemCode = o.OrderCode,
            ItemName = "",
            Status = p.DueDate == null || p.DueDate >= today ? "Current"
                : p.DueDate >= d7 ? "Overdue_1_7"
                : p.DueDate >= d30 ? "Overdue_8_30" : "Overdue_31_Plus",
            GrossAmount = p.AdjustedAmount,
            DiscountAmount = p.ConfirmedAmount,
            NetAmount = p.AdjustedAmount - p.ConfirmedAmount,
            OrderId = o.Id, OrderCode = o.OrderCode,
            Note = p.Note
        };
}
