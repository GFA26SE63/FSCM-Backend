using FSCM.Domain.Authorization;

namespace FSCM.Application.Features.Reports;

/// <summary>
/// F76: builds the Sales ranking for a closed period from recognized revenue after promotions and
/// F64 adjustments. Uses the same delivered-order + payment-ledger source the KPI reads (F75) so the
/// numbers do not diverge. Results are scoped: Admin/Operator see all reps, a SalesManager sees reps
/// in their own active group(s), and a Sales rep sees only their own group(s).
/// </summary>
public sealed class ReportService(IReportStore store, TimeProvider timeProvider)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<SalesRankingResult> GetSalesRankingAsync(
        SalesRankingQuery query, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator or
            SystemRoles.SalesManager or SystemRoles.Sales))
            throw ReportOperationException.Forbidden("Role cannot view the sales ranking.");
        if (query.ToDate < query.FromDate)
            throw ReportOperationException.Validation("The end date cannot be before the start date.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);
        if (query.FromDate > today)
            throw ReportOperationException.Validation("The period cannot start in the future.");

        var (groupIds, scope) = await ResolveScopeAsync(query.SalesGroupId, actorUserId, actorRole, cancellationToken);
        var rows = await store.AggregateSalesRevenueAsync(query.FromDate, query.ToDate, groupIds, cancellationToken);

        var ranked = rows
            .OrderByDescending(row => row.RecognizedRevenue)
            .ThenBy(row => row.SalesUserName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SalesUserId)
            .Select((row, index) => new SalesRankingEntryResult(
                index + 1, row.SalesUserId, row.SalesUserName, row.SalesGroupId,
                row.SalesGroupCode, row.SalesGroupName, row.RegionId,
                row.OrderCount, row.RecognizedRevenue))
            .ToArray();

        return new SalesRankingResult(query.FromDate, query.ToDate, scope, ranked);
    }

    private async Task<(IReadOnlyCollection<Guid>? GroupIds, string Scope)> ResolveScopeAsync(
        Guid? requestedGroupId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is SystemRoles.Admin or SystemRoles.Operator)
        {
            if (requestedGroupId.HasValue) return ([requestedGroupId.Value], "Group");
            return (null, "All");
        }

        // SalesManager and Sales are both bounded to their own active group memberships.
        var memberGroups = await store.ListActiveGroupIdsAsync(actorUserId, cancellationToken);
        if (memberGroups.Count == 0)
            throw ReportOperationException.Forbidden("You are not an active member of any sales group.");
        if (requestedGroupId.HasValue)
        {
            if (!memberGroups.Contains(requestedGroupId.Value))
                throw ReportOperationException.Forbidden("You cannot view the ranking for this sales group.");
            return ([requestedGroupId.Value], "Group");
        }
        return (memberGroups, actorRole == SystemRoles.SalesManager ? "Group" : "OwnGroup");
    }
}
