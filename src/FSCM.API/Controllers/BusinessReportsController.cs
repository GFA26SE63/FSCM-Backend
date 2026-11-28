using System.Security.Claims;
using FSCM.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/reports")]
public sealed class BusinessReportsController(IBusinessReportStore store) : ControllerBase
{
    [HttpGet("financial")]
    public Task<FinancialReportResult> Financial([FromQuery] FinancialReportQuery query, CancellationToken ct)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        ValidatePaging(query.Page, query.PageSize);
        if (query.Tab is not ("orders" or "receipts" or "refunds"))
            throw ReportOperationException.Validation("Tab must be orders, receipts, or refunds.");
        return store.GetFinancialAsync(query, ActorId(), ActorRole(), ct);
    }

    [HttpGet("receivables")]
    public Task<ReceivablesReportResult> Receivables([FromQuery] ReceivablesReportQuery query, CancellationToken ct)
    {
        if (query.DueFrom.HasValue && query.DueTo.HasValue)
            ValidatePeriod(query.DueFrom.Value, query.DueTo.Value);
        ValidatePaging(query.Page, query.PageSize);
        if (query.View is not ("invoices" or "customers"))
            throw ReportOperationException.Validation("View must be invoices or customers.");
        return store.GetReceivablesAsync(query, ActorId(), ActorRole(), ct);
    }

    [HttpGet("promotions")]
    public Task<PromotionsReportResult> Promotions([FromQuery] PromotionsReportQuery query, CancellationToken ct)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        ValidatePaging(query.Page, query.PageSize);
        return store.GetPromotionsAsync(query, ActorId(), ActorRole(), ct);
    }

    [HttpGet("loyalty")]
    public Task<LoyaltyReportResult> Loyalty([FromQuery] LoyaltyReportQuery query, CancellationToken ct)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        ValidatePaging(query.Page, query.PageSize);
        if (query.Tab is not ("redemptions" or "ledger" or "balances"))
            throw ReportOperationException.Validation("Tab must be redemptions, ledger, or balances.");
        return store.GetLoyaltyAsync(query, ActorId(), ActorRole(), ct);
    }

    [HttpGet("claims")]
    public Task<ClaimsReportResult> Claims([FromQuery] ClaimsReportQuery query, CancellationToken ct)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        ValidatePaging(query.Page, query.PageSize);
        return store.GetClaimsAsync(query, ActorId(), ActorRole(), ct);
    }

    [HttpGet("fulfillment")]
    public Task<FulfillmentReportResult> Fulfillment([FromQuery] FulfillmentReportQuery query, CancellationToken ct)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        ValidatePaging(query.Page, query.PageSize);
        if (query.Tab is not ("shipments" or "drivers" or "inbound" or "picking" or "transfers"))
            throw ReportOperationException.Validation("Tab must be shipments, drivers, inbound, picking, or transfers.");
        return store.GetFulfillmentAsync(query, ActorId(), ActorRole(), ct);
    }

    [HttpGet("inventory-risk")]
    public Task<InventoryRiskReportResult> InventoryRisk([FromQuery] InventoryRiskReportQuery query, CancellationToken ct)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        ValidatePaging(query.Page, query.PageSize);
        if (query.Tab is not ("expiry" or "locks" or "disposals"))
            throw ReportOperationException.Validation("Tab must be expiry, locks, or disposals.");
        return store.GetInventoryRiskAsync(query, ActorId(), ActorRole(), ct);
    }

    private static void ValidatePeriod(DateOnly fromDate, DateOnly toDate)
    {
        if (fromDate == default || toDate == default || toDate < fromDate ||
            toDate == DateOnly.MaxValue ||
            toDate.DayNumber - fromDate.DayNumber >= ReportPaginationLimits.MaxPeriodDays)
            throw ReportOperationException.Validation("Select a valid reporting period of at most 367 days.");
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || page > 1_000_000 || pageSize is < 1 or > ReportPaginationLimits.MaxPageSize)
            throw ReportOperationException.Validation("Page must be positive; page size must be between 1 and 200.");
    }

    private Guid ActorId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
        ? uid
        : throw ReportOperationException.Validation("Authenticated user identifier is invalid.");

    private string ActorRole() => User.FindFirstValue(ClaimTypes.Role)
        ?? throw ReportOperationException.Validation("Authenticated user role is invalid.");
}
