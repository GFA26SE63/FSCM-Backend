using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class BusinessReportsApiTests
{
    [Fact]
    public async Task All_typed_business_reports_enforce_roles_validate_inputs_and_bind_filters()
    {
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new RecordingStore();
        using var factory = identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBusinessReportStore>();
            services.AddSingleton<IBusinessReportStore>(store);
        }));
        using var client = factory.CreateClient();

        const string period = "fromDate=2026-11-01&toDate=2026-11-12";

        // Unauthenticated
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/reports/financial?{period}")).StatusCode);

        // Sales (Mobile-only) cannot access Portal Business Reports
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.Sales));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/reports/financial?{period}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/reports/inventory-risk?{period}")).StatusCode);

        // WarehouseKeeper cannot view Financial / Receivables / Promotions / Loyalty / Claims (RBAC leak fix)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.WarehouseKeeper));
        foreach (var commercial in new[] { "financial", "receivables", "promotions", "loyalty", "claims" })
        {
            var url = commercial == "receivables" ? "/api/v1/reports/receivables" : $"/api/v1/reports/{commercial}?{period}";
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        }
        // WarehouseKeeper CAN view Fulfillment and Inventory-Risk
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/reports/fulfillment?{period}&tab=shipments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/reports/inventory-risk?{period}&tab=expiry")).StatusCode);

        // SalesManager CAN view commercial reports and Fulfillment shipments, CANNOT view Inventory-Risk
        var sm = identity.AddTestUser("reports.sm@test.local", SystemRoles.SalesManager);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(sm));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/reports/financial?{period}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/reports/receivables?view=customers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/reports/fulfillment?{period}&tab=shipments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/reports/inventory-risk?{period}")).StatusCode);

        // Operator & Admin CAN view every report and bind parameters cleanly
        var op = identity.AddTestUser("reports.operator@test.local", SystemRoles.Operator);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(op));
        foreach (var path in new[]
        {
            $"/api/v1/reports/financial?{period}&tab=receipts&page=2&pageSize=20",
            "/api/v1/reports/receivables?view=invoices&bucket=Overdue_1_7&includeSettled=true",
            $"/api/v1/reports/promotions?{period}&exactProgramCode=KM01&completedOnly=true",
            $"/api/v1/reports/loyalty?{period}&tab=balances&retailer=RT01",
            $"/api/v1/reports/claims?{period}&requestType=Return&status=Open",
            $"/api/v1/reports/fulfillment?{period}&tab=picking",
            $"/api/v1/reports/inventory-risk?{period}&tab=locks",
        })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        }

        Assert.Equal(new DateOnly(2026, 11, 1), store.LastFinancialQuery?.FromDate);
        Assert.Equal("receipts", store.LastFinancialQuery?.Tab);
        Assert.Equal(2, store.LastFinancialQuery?.Page);
        Assert.Equal("KM01", store.LastPromotionsQuery?.ExactProgramCode);
        Assert.True(store.LastPromotionsQuery?.CompletedOnly);
        Assert.Equal("balances", store.LastLoyaltyQuery?.Tab);

        // Validation errors (400 BadRequest)
        foreach (var invalid in new[]
        {
            "/api/v1/reports/financial?fromDate=2026-11-12&toDate=2026-11-01",
            "/api/v1/reports/financial?fromDate=2026-11-01&toDate=2028-11-01",
            $"/api/v1/reports/financial?{period}&tab=unknown",
            "/api/v1/reports/receivables?view=bad",
            $"/api/v1/reports/loyalty?{period}&page=0",
            $"/api/v1/reports/loyalty?{period}&pageSize=201",
            $"/api/v1/reports/fulfillment?{period}&tab=invalid",
            $"/api/v1/reports/inventory-risk?{period}&tab=invalid",
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(invalid)).StatusCode);
        }

        // Dynamic RBAC: revoking api:BusinessReports.Financial blocks even Operator until re-granted
        identity.SetPermissions(SystemRoles.Operator, "reports");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/reports/financial?{period}")).StatusCode);
        identity.SetPermissions(SystemRoles.Operator, SystemRoleGrants.For(SystemRoles.Operator).ToArray());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/reports/financial?{period}")).StatusCode);
    }

    private sealed class RecordingStore : IBusinessReportStore
    {
        public FinancialReportQuery? LastFinancialQuery { get; private set; }
        public PromotionsReportQuery? LastPromotionsQuery { get; private set; }
        public LoyaltyReportQuery? LastLoyaltyQuery { get; private set; }

        public Task<FinancialReportResult> GetFinancialAsync(FinancialReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct)
        {
            LastFinancialQuery = query;
            return Task.FromResult(new FinancialReportResult(
                query.FromDate, query.ToDate, query.Tab, "All",
                new FinancialSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true, 0),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize),
                []));
        }

        public Task<ReceivablesReportResult> GetReceivablesAsync(ReceivablesReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct) =>
            Task.FromResult(new ReceivablesReportResult(
                new DateOnly(2026, 11, 12), query.View, "All",
                new ReceivablesSummary(0, 0, 0, 0, 0, 0, 0, 0, 0), [],
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize), []));

        public Task<PromotionsReportResult> GetPromotionsAsync(PromotionsReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct)
        {
            LastPromotionsQuery = query;
            return Task.FromResult(new PromotionsReportResult(
                query.FromDate, query.ToDate, "All",
                new PromotionsSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [],
                new([], 0, query.Page, query.PageSize), []));
        }

        public Task<LoyaltyReportResult> GetLoyaltyAsync(LoyaltyReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct)
        {
            LastLoyaltyQuery = query;
            return Task.FromResult(new LoyaltyReportResult(
                query.FromDate, query.ToDate, query.Tab, "All",
                new LoyaltyReportSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize), []));
        }

        public Task<ClaimsReportResult> GetClaimsAsync(ClaimsReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct) =>
            Task.FromResult(new ClaimsReportResult(
                query.FromDate, query.ToDate, "All",
                new ClaimsSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [],
                new([], 0, query.Page, query.PageSize), []));

        public Task<FulfillmentReportResult> GetFulfillmentAsync(FulfillmentReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct) =>
            Task.FromResult(new FulfillmentReportResult(
                query.FromDate, query.ToDate, query.Tab, "All",
                new FulfillmentSummary(0, 0, 0, 0, 0, 0, null, null, null, null, null, null),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize), []));

        public Task<InventoryRiskReportResult> GetInventoryRiskAsync(InventoryRiskReportQuery query, Guid actorUserId, string actorRole, CancellationToken ct) =>
            Task.FromResult(new InventoryRiskReportResult(
                query.FromDate, query.ToDate, query.Tab, "All",
                new InventoryRiskSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize),
                new([], 0, query.Page, query.PageSize), []));
    }
}
