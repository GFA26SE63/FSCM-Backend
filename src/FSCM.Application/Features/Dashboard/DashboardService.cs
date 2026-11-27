using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;

namespace FSCM.Application.Features.Dashboard;

public sealed class DashboardService(IDashboardStore store, TimeProvider clock, ReportService? reports = null)
{
    public async Task<DashboardResult> GetAsync(Guid actor, string role, DashboardQuery query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var from = query.FromDate ?? new DateOnly(today.Year, today.Month, 1);
        var to = query.ToDate ?? today;
        if (to < from || to > today || to.DayNumber - from.DayNumber > 365)
            throw ReportOperationException.Validation("Chọn kỳ báo cáo hợp lệ, tối đa 366 ngày và không vượt hôm nay.");
        var result = await store.ReadAsync(actor, role, query with { FromDate = from, ToDate = to }, now, ct);
        if (reports is not null && role != SystemRoles.WarehouseKeeper && result.Scope.HasAssignments)
        {
            try
            {
                var kpi = await reports.GetSalesKpiAsync(new(from, to, query.SalesGroupId, null), actor, role, ct);
                var scores = kpi.Entries.ToDictionary(e => (e.SalesUserId, e.SalesGroupId), e => e.OverallCompletionRate * 100m);
                // A member without a KPI entry has no target for the period: show "Chưa cấu hình", not 0%.
                result = result with { SalesTeam = result.SalesTeam.Select(s => s with
                    { KpiCompletion = scores.TryGetValue((s.Id, s.GroupId), out var score) ? score : null }).ToArray() };
            }
            catch (ReportOperationException e) when (e.Code == "validation_error")
            { result = result with { Notes = result.Notes.Append("Chưa có cấu hình KPI hợp lệ cho kỳ này; bảng nhóm vẫn hiển thị doanh thu và số đơn thực tế.").ToArray() }; }
        }
        return result;
    }
}
