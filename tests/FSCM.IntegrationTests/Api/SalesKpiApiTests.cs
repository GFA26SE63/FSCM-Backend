using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class SalesKpiApiTests
{
    [Fact]
    public async Task Rest_and_graphql_share_the_scoped_versioned_kpi_contract()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var sales = identityFactory.AddTestUser("sales.kpi@test.local", SystemRoles.Sales);
        var store = new TestStore(sales.Id);
        using var factory = identityFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportStore>();
            services.AddSingleton<IReportStore>(store);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(sales, identityFactory);

        var rest = await client.GetAsync("/api/v1/reports/sales-kpis?fromDate=2026-10-01&toDate=2026-10-31");
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement;
        var restEntry = restBody.GetProperty("entries")[0];
        Assert.Equal(3, restBody.GetProperty("configurationVersion").GetInt32());
        Assert.Equal(store.OrderId, restEntry.GetProperty("orders")[0].GetProperty("orderId").GetGuid());
        Assert.Equal(850_000, restEntry.GetProperty("recognizedRevenue").GetDecimal());

        var forbiddenConfiguration = await client.PutAsJsonAsync("/api/v1/configurations/reporting.sales_kpi", new
        {
            value = new { successfulOrdersTarget = 15 }
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenConfiguration.StatusCode);

        var graphQl = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ salesKpis(query: { fromDate: \"2026-10-01\", toDate: \"2026-10-31\" }) { configurationVersion entries { salesUserId recognizedRevenue orders { orderId } } } }"
        });
        Assert.Equal(HttpStatusCode.OK, graphQl.StatusCode);
        var graphBody = JsonDocument.Parse(await graphQl.Content.ReadAsStringAsync()).RootElement;
        Assert.False(graphBody.TryGetProperty("errors", out _), graphBody.ToString());
        var graphEntry = graphBody.GetProperty("data").GetProperty("salesKpis").GetProperty("entries")[0];
        Assert.Equal(restEntry.GetProperty("recognizedRevenue").GetDecimal(),
            graphEntry.GetProperty("recognizedRevenue").GetDecimal());

        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.WarehouseKeeper, identityFactory);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/reports/sales-kpis?fromDate=2026-10-01&toDate=2026-10-31")).StatusCode);
    }

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class TestStore : IReportStore
    {
        private readonly Guid salesId;
        private readonly Guid groupId = Guid.NewGuid();

        public TestStore(Guid salesId)
        {
            this.salesId = salesId;
            OrderId = Guid.NewGuid();
        }

        public Guid OrderId { get; }

        public Task<IReadOnlyCollection<Guid>> ListActiveGroupIdsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(userId == salesId ? [groupId] : []);

        public Task<bool> IsActiveGroupMemberAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == salesId && salesGroupId == groupId);

        public Task<IReadOnlyCollection<SalesRevenueRow>> AggregateSalesRevenueAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SalesRevenueRow>>([]);

        public Task<IReadOnlyCollection<SalesKpiOrderRow>> ListRecognizedOrdersAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            Guid? salesUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SalesKpiOrderRow>>([
                new(OrderId, "DH-001075", Guid.NewGuid(), "10004128", "Siêu thị Bình Minh",
                    salesId, "Nguyễn Hải", groupId, "HCM-A", "Nhóm HCM A", Guid.NewGuid(),
                    new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 5),
                    1_000_000, 100_000, 900_000, 50_000, 850_000)
            ]);

        public Task<SalesKpiConfigurationRow?> FindSalesKpiConfigurationAsync(
            DateOnly periodEnd, CancellationToken cancellationToken) =>
            Task.FromResult<SalesKpiConfigurationRow?>(new(
                "{\"successfulOrdersTarget\":15,\"retailersServedTarget\":5,\"newRetailersTarget\":1," +
                "\"promotionRevenueRatioMaximum\":0.05,\"successfulOrdersWeight\":0.25," +
                "\"retailersServedWeight\":0.25,\"newRetailersWeight\":0.25," +
                "\"promotionRevenueRatioWeight\":0.25}", 3, DateTimeOffset.Parse("2026-10-01T00:00:00Z")));

        public Task<IReadOnlyCollection<ProductSalesRow>> AggregateProductSalesAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductSalesRow>>([]);
    }
}
