using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FSCM.Application.Features.Dashboard;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

public sealed class DashboardApiTests
{
    [Theory]
    [InlineData(SystemRoles.Admin)]
    [InlineData(SystemRoles.Operator)]
    [InlineData(SystemRoles.WarehouseKeeper)]
    [InlineData(SystemRoles.SalesManager)]
    public async Task Dashboard_uses_authenticated_actor_and_accepts_only_real_filter_contract(string role)
    {
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        var user = identity.AddTestUser($"dashboard-{role}@test.local", role);
        var store = new TestStore();
        using var factory = identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IDashboardStore>(); services.AddSingleton<IDashboardStore>(store); }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(user));
        var region = Guid.NewGuid(); var warehouse = Guid.NewGuid(); var group = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/dashboard?fromDate=2026-10-01&toDate=2026-10-31&regionId={region}&warehouseId={warehouse}&salesGroupId={group}&role=Admin&userId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(user.Id,store.Actor); Assert.Equal(role,store.Role);
        Assert.Equal(region,store.Query!.RegionId); Assert.Equal(warehouse,store.Query.WarehouseId); Assert.Equal(group,store.Query.SalesGroupId);
        var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(role,result.GetProperty("role").GetString());
        Assert.Equal("2026-10-01",result.GetProperty("fromDate").GetString());
    }

    [Theory]
    [InlineData(SystemRoles.Sales)]
    [InlineData(SystemRoles.Retailer)]
    public async Task Mobile_roles_cannot_read_Portal_dashboard(string role)
    {
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        var user=identity.AddTestUser($"dashboard-{role}@test.local",role);
        using var client=identity.CreateClient();
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",identity.CreateToken(user));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_and_invalid_periods_are_rejected_before_the_store_is_called()
    {
        using var identity=new IdentityAuthorizationTests.IdentityApiFactory();var store=new TestStore();
        using var factory=identity.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        { services.RemoveAll<IDashboardStore>();services.AddSingleton<IDashboardStore>(store); }));
        using var client=factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/dashboard")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",identity.CreateToken(identity.WarehouseKeeper));
        foreach(var filter in new[]{"fromDate=2026-10-31&toDate=2026-10-01","fromDate=2020-01-01&toDate=2026-01-01","toDate=2099-01-01","regionId=invalid"})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/dashboard?{filter}")).StatusCode);
        Assert.Null(store.Query);
    }

    private sealed class TestStore : IDashboardStore
    {
        public Guid Actor; public string? Role; public DashboardQuery? Query;
        public Task<DashboardResult> ReadAsync(Guid actor,string role,DashboardQuery query,DateTimeOffset now,CancellationToken ct)
        {
            Actor=actor;Role=role;Query=query;
            return Task.FromResult(new DashboardResult(role,now,query.FromDate!.Value,query.ToDate!.Value,
                new(false,false,"No assignments",[],[],[]),[],[],[],[],[],[],[],[],[],null,null,[]));
        }
    }
}
