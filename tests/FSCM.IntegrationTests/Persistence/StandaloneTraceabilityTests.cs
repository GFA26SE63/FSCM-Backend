using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fscm.Traceability.Data;
using Fscm.Traceability.Models;
using Fscm.Traceability.Services;
using FSCM.Application.Features.Picking;
using FSCM.Persistence.WarehouseWork;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed class StandaloneTraceabilityTests
{
    [Fact]
    public void Factory_content_root_resolves_when_both_artifacts_and_working_directory_are_outside_checkout()
    {
        var outside = Path.Combine(Path.GetTempPath(), "fscm-content-root-test", "artifacts", "bin");
        var root = StandaloneFactoryFixture.ResolveContentRoot(outside, Path.GetTempPath());
        Assert.True(File.Exists(Path.Combine(root, "FSCM.Traceability.csproj")));
        Assert.True(File.Exists(Path.Combine(root, "wwwroot", "index.html")));
        Assert.Throws<InvalidOperationException>(() => StandaloneFactoryFixture.ResolveContentRoot(outside,
            Path.GetTempPath(), sourceFile: ""));
    }

    [Fact]
    public void Batch_mapping_queries_translate_parent_links_filters_and_paging_to_postgres()
    {
        using var db = new FactoryDbContext(new DbContextOptionsBuilder<FactoryDbContext>().UseNpgsql("Host=127.0.0.1;Database=unused_factory;Username=unused").Options);
        var service = new ProductionService(db, new FakeFscm(), TimeProvider.System);
        foreach (var level in new[] { "cartons", "units" })
        {
            var query = service.MappingQuery(Guid.NewGuid(), level, "PID-test", "CID-test", "001")
                .OrderBy(x => x.PalletCode).ThenBy(x => x.CartonCode);
            if (level == "units") query = query.ThenBy(x => x.UnitCode);
            var sql = query.Skip(50).Take(50).ToQueryString();
            Assert.Contains("FACTORY_CARTON", sql); Assert.Contains("FACTORY_PALLET", sql);
            Assert.Contains("FACTORY_PRODUCTION_BATCH", sql); Assert.Contains("FACTORY_PRODUCT_UNIT", sql);
            Assert.Contains("LIMIT", sql); Assert.Contains("OFFSET", sql);
        }
    }

    [Fact]
    public void Mapping_pagination_rejects_invalid_level_negative_pages_and_unbounded_rows()
    {
        ProductionService.ValidateMappingPage("cartons", 1, 50);
        ProductionService.ValidateMappingPage("units", 2, 100);
        foreach (var value in new[] { ("pallets", 1, 50), ("cartons", 0, 50), ("units", 1, 201), ("units", int.MaxValue, 200) })
            Assert.Throws<FactoryException>(() => ProductionService.ValidateMappingPage(value.Item1, value.Item2, value.Item3));
    }

    [Fact]
    public void Factory_assembly_builds_without_FSCM_runtime_project_dependencies()
    {
        Assert.DoesNotContain(typeof(Fscm.Traceability.Program).Assembly.GetReferencedAssemblies(),
            x => x.Name is "FSCM.API" or "FSCM.Persistence" or "FSCM.Application" or "FSCM.Domain" or "FSCM.Infrastructure");
    }

    [Fact]
    public void Standalone_model_keeps_existing_factory_schema_and_migration_history()
    {
        using var db = new FactoryDbContext(new DbContextOptionsBuilder<FactoryDbContext>().UseNpgsql("Host=127.0.0.1;Database=unused_factory;Username=unused").Options);
        Assert.Equal(new[] { "FACTORY_CARTON", "FACTORY_PALLET", "FACTORY_PRODUCTION_BATCH", "FACTORY_PRODUCT_UNIT" },
            db.Model.GetEntityTypes().Select(x => x.GetTableName()).Order(StringComparer.Ordinal).ToArray());
        using var original = new FSCM.Persistence.DbContexts.TraceabilityDbContext(
            new DbContextOptionsBuilder<FSCM.Persistence.DbContexts.TraceabilityDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=unused_factory;Username=unused").Options);
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.Equal(original.Database.GetMigrations().ToArray(), migrations);
        Assert.Equal("20261113064338_InitialFactoryDatabase", migrations[0]);
        Assert.Contains("20261114190000_AddReceiptInspection", migrations);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.False(original.Database.HasPendingModelChanges());
        Assert.All(db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()), fk => Assert.StartsWith("FACTORY_", fk.PrincipalEntityType.GetTableName()));
    }

    [Fact]
    public async Task Standalone_health_is_independent_and_integration_requires_a_service_key()
    {
        await using var factory = new StandaloneFactoryFixture("Host=127.0.0.1;Database=unused_factory;Username=unused", new FakeFscm());
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/batches")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/integration/pallets/PID-test/manifest")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/integration/dispatches?warehouseIds={Guid.NewGuid()}")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Traceability-Key", "wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/integration/trace?code=PID-test")).StatusCode);
    }

    [Fact]
    public async Task Admin_session_uses_httponly_cookie_and_never_returns_tokens_to_frontend()
    {
        await using var factory = new StandaloneFactoryFixture("Host=127.0.0.1;Database=unused_factory;Username=unused", new FakeFscm());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "Traceability");
        using var response = await client.PostAsJsonAsync("/api/session/login", new Fscm.Traceability.Models.LoginRequest("admin-test", "test-only"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("test-access", content); Assert.DoesNotContain("test-refresh", content);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), x => x.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/catalog/products")).StatusCode);
    }

    [Fact]
    public async Task Non_admin_cannot_sign_in_to_factory_and_browser_mutations_require_custom_header()
    {
        await using var factory = new StandaloneFactoryFixture("Host=127.0.0.1;Database=unused_factory;Username=unused", new FakeFscm("WarehouseKeeper"));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/session/login", new Fscm.Traceability.Models.LoginRequest("user", "test"))).StatusCode);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Traceability");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/session/login", new Fscm.Traceability.Models.LoginRequest("user", "test"))).StatusCode);
    }

    [Fact]
    public async Task WMS_gateway_sends_receipt_ack_over_http_without_exposing_credentials_in_url()
    {
        var pallet = Guid.NewGuid(); var receipt = Guid.NewGuid(); var warehouse = Guid.NewGuid();
        var handler = new RecordingHandler(async request => {
            Assert.Equal("https://factory.test/api/integration/receipts", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("test-integration", request.Headers.GetValues("X-Traceability-Key").Single());
            var payload = await request.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(pallet, payload.GetProperty("palletId").GetGuid());
            Assert.Equal(receipt, payload.GetProperty("receiptDocumentId").GetGuid());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = pallet }) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://factory.test/") };
        http.DefaultRequestHeaders.Add("X-Traceability-Key", "test-integration");
        var result = await new FactoryHttpGateway(http).AcknowledgeReceiptAsync(new FSCM.Domain.Entities.FactoryReceiptSync {
            PalletId = pallet, ReceiptDocumentId = receipt, WarehouseId = warehouse, ReceivedAt = DateTimeOffset.UtcNow }, default);
        Assert.Equal(pallet, result);
    }

    [Fact]
    public async Task WMS_gateway_surfaces_missing_manifest_as_not_found()
    {
        using var http = new HttpClient(new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))) { BaseAddress = new Uri("https://factory.test/") };
        var error = await Assert.ThrowsAsync<PickingOperationException>(() => new FactoryHttpGateway(http).ManifestAsync("PID-test", default));
        Assert.Equal("not_found", error.Code);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private sealed class FakeFscm(string role = "Admin") : IFscmGateway
    {
        private readonly Guid id = Guid.NewGuid();
        public Task<FscmUser> MeAsync(string token, CancellationToken ct) => Task.FromResult(new FscmUser(id, "test@example.test", "Factory Admin", role));
        public async Task<FscmSession> LoginAsync(Fscm.Traceability.Models.LoginRequest request, CancellationToken ct) => new("test-access", DateTimeOffset.UtcNow.AddHours(1), "test-refresh", DateTimeOffset.UtcNow.AddDays(1), await MeAsync("", ct));
        public Task<FscmSession> RefreshAsync(string token, CancellationToken ct) => LoginAsync(new("test", "test"), ct);
        public Task<FscmProduct[]> ProductsAsync(string token, CancellationToken ct) => Task.FromResult(Array.Empty<FscmProduct>());
        public Task<FscmWarehouse[]> WarehousesAsync(string token, CancellationToken ct) => Task.FromResult(Array.Empty<FscmWarehouse>());
        public Task<FscmProduct> ProductAsync(Guid id, string token, CancellationToken ct) => Task.FromResult(new FscmProduct(id, "SKU001", "Test product", "Published"));
        public Task<FscmWarehouse> WarehouseAsync(Guid id, string token, CancellationToken ct) => Task.FromResult(new FscmWarehouse(id, "WH01", "Test warehouse", true));
    }
}
