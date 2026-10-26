using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;

namespace FSCM.UnitTests.Reports;

public sealed class ReportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public async Task Admin_sees_all_reps_ranked_by_recognized_revenue_desc()
    {
        var fixture = new Fixture();
        fixture.AddRevenue(fixture.GroupA, "Nguyễn A", 1_000_000, orders: 3);
        fixture.AddRevenue(fixture.GroupA, "Trần B", 2_500_000, orders: 5);
        fixture.AddRevenue(fixture.GroupB, "Lê C", 1_800_000, orders: 4);

        var result = await fixture.Service.GetSalesRankingAsync(
            new SalesRankingQuery(From, To, null), Guid.NewGuid(), SystemRoles.Admin, default);

        Assert.Equal("All", result.Scope);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(["Trần B", "Lê C", "Nguyễn A"], result.Entries.Select(e => e.SalesUserName));
        Assert.Equal([1, 2, 3], result.Entries.Select(e => e.Rank));
        Assert.Equal(2_500_000, result.Entries.First().RecognizedRevenue);
        Assert.Equal(5, result.Entries.First().OrderCount);
    }

    [Fact]
    public async Task Sales_manager_only_sees_own_group_members()
    {
        var fixture = new Fixture();
        var managerRow = fixture.AddRevenue(fixture.GroupA, "Quản lý A", 500_000, orders: 1);
        fixture.AddRevenue(fixture.GroupA, "Nhân viên A", 900_000, orders: 2);
        fixture.AddRevenue(fixture.GroupB, "Nhân viên B", 3_000_000, orders: 6);
        fixture.Store.MemberGroups[managerRow.SalesUserId] = [fixture.GroupA];

        var result = await fixture.Service.GetSalesRankingAsync(
            new SalesRankingQuery(From, To, null), managerRow.SalesUserId, SystemRoles.SalesManager, default);

        Assert.Equal("Group", result.Scope);
        Assert.All(result.Entries, e => Assert.Equal(fixture.GroupA, e.SalesGroupId));
        Assert.DoesNotContain(result.Entries, e => e.SalesUserName == "Nhân viên B");
    }

    [Fact]
    public async Task Sales_manager_cannot_query_a_group_they_do_not_belong_to()
    {
        var fixture = new Fixture();
        var manager = Guid.NewGuid();
        fixture.Store.MemberGroups[manager] = [fixture.GroupA];

        var exception = await Assert.ThrowsAsync<ReportOperationException>(() =>
            fixture.Service.GetSalesRankingAsync(
                new SalesRankingQuery(From, To, fixture.GroupB), manager, SystemRoles.SalesManager, default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Sales_with_no_active_group_is_forbidden()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ReportOperationException>(() =>
            fixture.Service.GetSalesRankingAsync(
                new SalesRankingQuery(From, To, null), Guid.NewGuid(), SystemRoles.Sales, default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Retailer_role_cannot_view_ranking()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ReportOperationException>(() =>
            fixture.Service.GetSalesRankingAsync(
                new SalesRankingQuery(From, To, null), Guid.NewGuid(), SystemRoles.Retailer, default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Reversed_date_range_is_rejected()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ReportOperationException>(() =>
            fixture.Service.GetSalesRankingAsync(
                new SalesRankingQuery(To, From, null), Guid.NewGuid(), SystemRoles.Admin, default));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Sales_kpi_uses_versioned_targets_adjusted_revenue_and_order_drill_down()
    {
        var fixture = new Fixture();
        var salesId = Guid.NewGuid();
        fixture.Store.MemberGroups[salesId] = [fixture.GroupA];
        fixture.Store.KpiRows.Add(KpiOrder(salesId, fixture.GroupA, "DH-001", 1_000_000, 100_000, 50_000));
        fixture.Store.KpiRows.Add(KpiOrder(salesId, fixture.GroupA, "DH-002", 500_000, 0, 25_000));

        var result = await fixture.Service.GetSalesKpiAsync(
            new SalesKpiQuery(From, To, null, null), salesId, SystemRoles.Sales, default);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(1_325_000, entry.RecognizedRevenue);
        Assert.Equal(2, entry.Orders.Count);
        Assert.Equal(7, result.ConfigurationVersion);
        Assert.Equal("successfulOrders", entry.Metrics.First().Code);
        Assert.Equal(2, entry.Metrics.First().Actual);
        Assert.All(entry.Orders, order => Assert.NotEqual(Guid.Empty, order.OrderId));
    }

    [Fact]
    public async Task Sales_cannot_request_another_users_kpi()
    {
        var fixture = new Fixture();
        var salesId = Guid.NewGuid();
        fixture.Store.MemberGroups[salesId] = [fixture.GroupA];

        var exception = await Assert.ThrowsAsync<ReportOperationException>(() => fixture.Service.GetSalesKpiAsync(
            new SalesKpiQuery(From, To, null, Guid.NewGuid()), salesId, SystemRoles.Sales, default));

        Assert.Equal("forbidden", exception.Code);
    }

    private static SalesKpiOrderRow KpiOrder(
        Guid salesId, Guid groupId, string code, decimal beforePromotion, decimal promotion, decimal adjustment)
    {
        var retailerId = Guid.NewGuid();
        var afterPromotion = beforePromotion - promotion;
        return new(Guid.NewGuid(), code, retailerId, "10000001", "Điểm bán", salesId, "Nhân viên Sales",
            groupId, "HCM-A", "Nhóm HCM A", Guid.Empty, new DateOnly(2026, 10, 15),
            new DateOnly(2026, 10, 2), beforePromotion, promotion, afterPromotion, adjustment,
            afterPromotion - adjustment);
    }

    private sealed class Fixture
    {
        public Guid GroupA { get; } = Guid.NewGuid();
        public Guid GroupB { get; } = Guid.NewGuid();
        public FakeStore Store { get; } = new();
        public ReportService Service { get; }

        public Fixture() => Service = new ReportService(Store, new FixedTimeProvider(Now));

        public SalesRevenueRow AddRevenue(Guid groupId, string name, decimal revenue, int orders)
        {
            var row = new SalesRevenueRow(
                Guid.NewGuid(), name, groupId, "GRP", "Nhóm", Guid.NewGuid(), orders, revenue);
            Store.Rows.Add(row);
            return row;
        }
    }

    private sealed class FakeStore : IReportStore
    {
        public List<SalesRevenueRow> Rows { get; } = [];
        public Dictionary<Guid, Guid[]> MemberGroups { get; } = [];
        public List<SalesKpiOrderRow> KpiRows { get; } = [];

        public Task<IReadOnlyCollection<Guid>> ListActiveGroupIdsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(
                MemberGroups.TryGetValue(userId, out var groups) ? groups : []);

        public Task<bool> IsActiveGroupMemberAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken) =>
            Task.FromResult(MemberGroups.TryGetValue(userId, out var groups) && groups.Contains(salesGroupId));

        public Task<IReadOnlyCollection<SalesRevenueRow>> AggregateSalesRevenueAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            CancellationToken cancellationToken)
        {
            var set = salesGroupIds?.ToHashSet();
            return Task.FromResult<IReadOnlyCollection<SalesRevenueRow>>(
                Rows.Where(row => set == null || set.Contains(row.SalesGroupId)).ToArray());
        }

        public Task<IReadOnlyCollection<SalesKpiOrderRow>> ListRecognizedOrdersAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            Guid? salesUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SalesKpiOrderRow>>(KpiRows
                .Where(row => salesGroupIds == null || salesGroupIds.Contains(row.SalesGroupId))
                .Where(row => !salesUserId.HasValue || row.SalesUserId == salesUserId)
                .ToArray());

        public Task<SalesKpiConfigurationRow?> FindSalesKpiConfigurationAsync(
            DateOnly periodEnd, CancellationToken cancellationToken) =>
            Task.FromResult<SalesKpiConfigurationRow?>(new(
                "{\"successfulOrdersTarget\":15,\"retailersServedTarget\":5,\"newRetailersTarget\":1," +
                "\"promotionRevenueRatioMaximum\":0.05,\"successfulOrdersWeight\":0.25," +
                "\"retailersServedWeight\":0.25,\"newRetailersWeight\":0.25," +
                "\"promotionRevenueRatioWeight\":0.25}", 7, Now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
