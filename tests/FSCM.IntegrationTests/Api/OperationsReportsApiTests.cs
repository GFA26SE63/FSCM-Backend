using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FSCM.Application.Features.Reports;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class OperationsReportsApiTests
{
    [Fact]
    public async Task Operations_reports_enforce_roles_validate_dates_and_bind_filters()
    {
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        var store = new RecordingStore();
        using var factory = identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOperationsReportStore>();
            services.AddSingleton<IOperationsReportStore>(store);
        }));
        using var client = factory.CreateClient();
        const string path = "/api/v1/reports/operations/redemptions?fromDate=2026-11-01&toDate=2026-11-12";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.Sales));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.WarehouseKeeper));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.Admin));
        var operatorAccount = identity.AddTestUser("reports.operator@test.local", "Operator");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(operatorAccount));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.Admin));
        var result = await client.GetFromJsonAsync<OperationsReportResult>(path + "&retailer=ABC&program=LP01&status=Completed&page=2&pageSize=20");
        Assert.NotNull(result);
        Assert.Equal(new OperationsReportQuery(new(2026, 11, 1), new(2026, 11, 12), "ABC", "LP01", "Completed", null, 2, 20), store.LastQuery);
        foreach (var invalid in new[] {
            "/api/v1/reports/operations/unknown?fromDate=2026-11-01&toDate=2026-11-12",
            "/api/v1/reports/operations/loyalty?fromDate=2026-11-12&toDate=2026-11-01",
            "/api/v1/reports/operations/loyalty",
            path + "&page=0", path + "&pageSize=201", path + "&regionId=invalid",
            path + "&status=NotAStatus", path.Replace("toDate=2026-11-12", "toDate=2028-11-12") })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(invalid)).StatusCode);
        foreach (var kind in new[] { "loyalty", "promotions" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path.Replace("redemptions", kind))).StatusCode);
    }

    private sealed class RecordingStore : IOperationsReportStore
    {
        public OperationsReportQuery? LastQuery { get; private set; }
        public Task<OperationsReportResult> ReadAsync(string kind, OperationsReportQuery query, CancellationToken ct)
        {
            LastQuery = query;
            return Task.FromResult(new OperationsReportResult([], 0, query.Page, query.PageSize, [], []));
        }
    }
}
