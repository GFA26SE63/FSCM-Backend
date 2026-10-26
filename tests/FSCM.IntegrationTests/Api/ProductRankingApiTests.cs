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

public sealed class ProductRankingApiTests
{
    [Fact]
    public async Task Rest_and_graphql_share_the_scoped_product_ranking_contract()
    {
        using var identityFactory = new IdentityAuthorizationTests.IdentityApiFactory();
        var manager = identityFactory.AddTestUser("manager.ranking@test.local", SystemRoles.SalesManager);
        var store = new TestStore(manager.Id);
        using var factory = identityFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportStore>();
            services.AddSingleton<IReportStore>(store);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(manager, identityFactory);

        var rest = await client.GetAsync(
            $"/api/v1/reports/best-selling-products?fromDate=2026-10-01&toDate=2026-10-31&criterion=Revenue&salesGroupId={store.GroupId}");
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        var restBody = JsonDocument.Parse(await rest.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Revenue", restBody.GetProperty("criterion").GetString());
        Assert.Equal("carton", restBody.GetProperty("quantityUnit").GetString());
        Assert.Equal("SKU-002", restBody.GetProperty("entries")[0].GetProperty("sku").GetString());
        Assert.Equal(450_000, restBody.GetProperty("entries")[0].GetProperty("adjustedNetRevenue").GetDecimal());

        var graphQl = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ bestSellingProducts(query: { fromDate: \"2026-10-01\", toDate: \"2026-10-31\", " +
                    $"criterion: \"Revenue\", salesGroupId: \"{store.GroupId}\" }}) " +
                    "{ criterion quantityUnit entries { rank sku normalizedSoldQuantity adjustedNetRevenue } } }"
        });
        Assert.Equal(HttpStatusCode.OK, graphQl.StatusCode);
        var graphBody = JsonDocument.Parse(await graphQl.Content.ReadAsStringAsync()).RootElement;
        Assert.False(graphBody.TryGetProperty("errors", out _), graphBody.ToString());
        var graphResult = graphBody.GetProperty("data").GetProperty("bestSellingProducts");
        Assert.Equal(restBody.GetProperty("entries")[0].GetProperty("adjustedNetRevenue").GetDecimal(),
            graphResult.GetProperty("entries")[0].GetProperty("adjustedNetRevenue").GetDecimal());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(
            "/api/v1/reports/best-selling-products?fromDate=2026-10-01&toDate=2026-10-31&criterion=Profit")).StatusCode);
        client.DefaultRequestHeaders.Authorization = Bearer(identityFactory.WarehouseKeeper, identityFactory);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            "/api/v1/reports/best-selling-products?fromDate=2026-10-01&toDate=2026-10-31")).StatusCode);
    }

    private static AuthenticationHeaderValue Bearer(
        UserAccount user, IdentityAuthorizationTests.IdentityApiFactory factory) =>
        new("Bearer", factory.CreateToken(user));

    private sealed class TestStore(Guid managerId) : IReportStore
    {
        public Guid GroupId { get; } = Guid.NewGuid();

        public Task<IReadOnlyCollection<Guid>> ListActiveGroupIdsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(userId == managerId ? [GroupId] : []);
        public Task<bool> IsActiveGroupMemberAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == managerId && salesGroupId == GroupId);
        public Task<IReadOnlyCollection<SalesRevenueRow>> AggregateSalesRevenueAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SalesRevenueRow>>([]);
        public Task<IReadOnlyCollection<SalesKpiOrderRow>> ListRecognizedOrdersAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            Guid? salesUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SalesKpiOrderRow>>([]);
        public Task<SalesKpiConfigurationRow?> FindSalesKpiConfigurationAsync(
            DateOnly periodEnd, CancellationToken cancellationToken) =>
            Task.FromResult<SalesKpiConfigurationRow?>(null);
        public Task<IReadOnlyCollection<ProductSalesRow>> AggregateProductSalesAsync(
            DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProductSalesRow>>([
                new(Guid.NewGuid(), "SKU-001", "Sữa chua uống", 40, 300_000),
                new(Guid.NewGuid(), "SKU-002", "Nước ép trái cây", 25, 450_000)
            ]);
    }
}
