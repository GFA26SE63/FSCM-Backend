using System.Net;
using System.Net.Http.Json;
using FSCM.Application.Features.EntityQueries;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Api;

/// <summary>Exercise authentication, live grants, binding and resource filters without business writes.</summary>
public sealed class ResourceScopeAuthorizationTests
{
    [Fact]
    public async Task Receipt_requires_live_grants_and_parent_ownership_but_not_inventory_access()
    {
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        var retailer = identity.AddTestUser("receipt-scope@test.invalid", SystemRoles.Retailer);
        var source = new ScopedEntities();
        var shipment = new Shipment { Id = Guid.NewGuid() };
        var other = new Shipment { Id = Guid.NewGuid() };
        source.Add(shipment, true);
        source.Add(other, false);
        // This batch exists, but the retailer has no independent inventory scope.
        var batch = new Batch { Id = Guid.NewGuid() };
        source.Add(batch, false);
        using var factory = CreateProbeFactory(identity, source);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", identity.CreateToken(retailer));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var body = new
        {
            qrCodes = new[] { "CID-F01-20261127-A-0001-0005" },
            lines = new[] { new { orderLineId = Guid.NewGuid(), batchId = batch.Id, receivedQuantity = 1, damagedQuantity = 0 } },
            expectedVersion = 1
        };
        // Same JWT throughout; grants/mapping remain authoritative on each request.
        foreach (var granted in new[] { true, false, true })
        {
            identity.SetPermissions(SystemRoles.Retailer, granted
                ? ["mobile-rt-scan", "api:Shipments.ConfirmReceipt"] : ["mobile-rt-scan"]);
            var response = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receipt", body);
            Assert.Equal(granted ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/v1/shipments/{other.Id}/receipt", body)).StatusCode);
        identity.SetPermissions(SystemRoles.Retailer, "api:Shipments.ConfirmReceipt");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receipt", body)).StatusCode);
        Assert.DoesNotContain(typeof(Batch), source.ScopedReads);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateProbeFactory(
        IdentityAuthorizationTests.IdentityApiFactory identity, ScopedEntities source) =>
        identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEntityQueryService>();
            services.AddSingleton<IEntityQueryService>(source);
            services.Configure<MvcOptions>(options => options.Filters.Add(new ScopeProbe()));
        }));

    // Short-circuit only after the production authorization and scope filters have
    // run. Successful probes never execute a domain command or touch a database.
    private sealed class ScopeProbe : IActionFilter, IOrderedFilter
    {
        public int Order => int.MaxValue;
        public void OnActionExecuting(ActionExecutingContext context) => context.Result = new NoContentResult();
        public void OnActionExecuted(ActionExecutedContext context) { }
    }

    private sealed class ScopedEntities : IEntityQueryService
    {
        private readonly Dictionary<Type, List<object>> rows = [];
        private readonly HashSet<(Type, Guid)> allowed = [];
        public HashSet<Type> ScopedReads { get; } = [];

        public void Add<T>(T entity, bool inScope) where T : class
        {
            if (!rows.TryGetValue(typeof(T), out var list)) rows[typeof(T)] = list = [];
            list.Add(entity);
            if (inScope) allowed.Add((typeof(T), Id(entity)));
        }

        public IQueryable<T> Query<T>(EntityQueryAccess access) where T : class
        {
            if (access.Role != SystemRoles.Admin) ScopedReads.Add(typeof(T));
            var items = rows.GetValueOrDefault(typeof(T), []).Cast<T>();
            return items.Where(x => access.Role == SystemRoles.Admin || allowed.Contains((typeof(T), Id(x)))).AsQueryable();
        }

        private static Guid Id(object entity) => (Guid)entity.GetType().GetProperty("Id")!.GetValue(entity)!;
    }
}
