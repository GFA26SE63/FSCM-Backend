using System.Security.Claims;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/reports/operations")]
public sealed class OperationsReportsController(IOperationsReportStore store) : ControllerBase
{
    [HttpGet("{kind}")]
    public Task<OperationsReportResult> Read(string kind, [FromQuery] OperationsReportQuery query,
        CancellationToken ct)
    {
        if (kind is not ("redemptions" or "loyalty" or "promotions" or "disposals" or "claims" or "receivables"))
            throw ReportOperationException.Validation("Unknown report kind.");
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (kind == "disposals" && role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.WarehouseKeeper))
            throw ReportOperationException.Forbidden("Vai trò này không có quyền xem báo cáo hủy hàng.");
        if (kind != "disposals" && role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager))
            throw ReportOperationException.Forbidden("Vai trò này không có quyền xem báo cáo kinh doanh / chăm sóc khách hàng.");
        if (query.FromDate == default || query.ToDate == default || query.ToDate < query.FromDate ||
            query.ToDate.DayNumber - query.FromDate.DayNumber > 366 || query.ToDate == DateOnly.MaxValue)
            throw ReportOperationException.Validation("Select a valid reporting period of at most 367 days.");
        if (query.Page < 1 || query.Page > 1_000_000 || query.PageSize is < 1 or > 200)
            throw ReportOperationException.Validation("Page must be positive; page size must be between 1 and 200.");
        if (query.Retailer?.Length > 200 || query.Program?.Length > 200 || query.Status?.Length > 100)
            throw ReportOperationException.Validation("Report filter is too long.");
        if (kind == "redemptions" && !string.IsNullOrWhiteSpace(query.Status) &&
            !new[] { "Pending", "Approved", "Delivered", "Completed", "Rejected", "Cancelled" }.Contains(query.Status))
            throw ReportOperationException.Validation("Invalid redemption status.");
        if (kind == "promotions" && !string.IsNullOrWhiteSpace(query.Status) &&
            !new[] { "Allocating", "Quoted", "AllocationFailed", "OutOfStock", "AllocationConflict", "Withdrawn", "QuoteRejected", "QuoteExpired", "PaymentPending", "ConvertedToPo", "Picking", "Issued", "Delivering", "Delivered", "Completed" }.Contains(query.Status))
            throw ReportOperationException.Validation("Invalid order status.");
        Guid? actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
        return store.ReadAsync(kind, query, actorId, role, ct);
    }
}
