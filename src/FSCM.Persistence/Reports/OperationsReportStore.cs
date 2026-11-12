using FSCM.Application.Features.Reports;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Reports;

public sealed class OperationsReportStore(AppDbContext db) : IOperationsReportStore
{
    public async Task<OperationsReportResult> ReadAsync(string kind, OperationsReportQuery q, CancellationToken ct)
    {
        var start = new DateTimeOffset(q.FromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var end = new DateTimeOffset(q.ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        IQueryable<OperationsReportRow> source = kind switch
        {
            "redemptions" => Redemptions(),
            "loyalty" => Loyalty(),
            "promotions" => Promotions(),
            _ => throw ReportOperationException.Validation("Unknown report kind.")
        };
        if (q.RegionId.HasValue) source = source.Where(r => r.RegionId == q.RegionId);
        if (!string.IsNullOrWhiteSpace(q.Retailer))
        {
            var term = q.Retailer.Trim().ToLower();
            source = source.Where(r => r.RetailerCode.ToLower().Contains(term) || r.RetailerName.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(q.Program))
        {
            var term = q.Program.Trim().ToLower();
            source = source.Where(r => r.ProgramCode.ToLower().Contains(term) || r.ProgramName.ToLower().Contains(term));
        }
        // Balance totals use the entire filtered ledger, regardless of transaction-type filter.
        var ledger = source.Where(r => r.Status != "PendingEarn" && r.Status != "PendingEarnCancelled" && r.Status != "Pending");
        source = source.Where(r => r.At >= start && r.At < end);
        if (!string.IsNullOrWhiteSpace(q.Status)) source = source.Where(r => r.Status == q.Status);
        var count = await source.CountAsync(ct);
        var summary = new List<OperationsReportMetric> { new("Số bản ghi", count),
            new("Retailer", await source.Select(r => r.RetailerId).Distinct().CountAsync(ct)) };
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
        else
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
        var items = await source.OrderByDescending(r => r.At).ThenBy(r => r.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
        return new(items, count, q.Page, q.PageSize, summary, notes);
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
            ProgramCode = p.Code, ProgramName = p.Name, Status = x.TransactionType,
            Points = x.Points, BalanceAfter = x.BalanceAfter, OrderId = x.SalesOrderId,
            TransactionId = x.Id, CreatedByUserId = x.CreatedByUserId, Note = x.Note,
            OrderCode = db.SalesOrderRecords.Where(o => o.Id == x.SalesOrderId).Select(o => o.OrderCode).FirstOrDefault(),
            CreatedByName = db.UserAccountRecords.Where(u => u.Id == x.CreatedByUserId).Select(u => u.FullName).FirstOrDefault()
        };

    private IQueryable<OperationsReportRow> Promotions() =>
        from quote in db.SalesOrderQuoteRecords.AsNoTracking()
        where quote.AcceptedAt != null && !db.SalesOrderQuoteRecords.Any(other =>
            other.SalesOrderId == quote.SalesOrderId && other.AcceptedAt != null && other.RevisionNumber > quote.RevisionNumber)
        // Prices are persisted once per allocation run; plan-version IDs are compatibility-only, unmapped fields.
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
}
