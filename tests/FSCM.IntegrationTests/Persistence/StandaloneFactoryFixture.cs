using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.WarehouseWork;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Fscm.Traceability.Services;
using Fscm.Traceability.Models;

namespace FSCM.IntegrationTests.Persistence;

internal sealed class StandaloneFactoryFixture : WebApplicationFactory<Fscm.Traceability.Program>
{
    private readonly string connection;
    private readonly IFscmGateway gateway;
    private readonly string contentRoot;
    private readonly bool migrateDatabase;
    public StandaloneFactoryFixture(string connection, IFscmGateway gateway, bool migrateDatabase = true)
    {
        this.connection = connection; this.gateway = gateway; this.migrateDatabase = migrateDatabase;
        contentRoot = ResolveContentRoot(AppContext.BaseDirectory, Directory.GetCurrentDirectory());
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_FSCM_TRACEABILITY", contentRoot);
    }

    internal static string ResolveContentRoot(string baseDirectory, string currentDirectory,
        [CallerFilePath] string sourceFile = "")
    {
        // Isolated artifacts can live outside the checkout, and VSTest can change its working
        // directory. The compiled source location still identifies the checkout used to build.
        var roots = new[] { Path.GetDirectoryName(sourceFile), currentDirectory, baseDirectory };
        foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var directory = new DirectoryInfo(root!);
            while (directory is not null)
            {
                foreach (var relative in new[] { "FSCM-Traceability", "PackageCode/FSCM-Traceability", ".ci/traceability" })
                {
                    var candidate = Path.Combine(directory.FullName, relative);
                    if (File.Exists(Path.Combine(candidate, "FSCM.Traceability.csproj"))) return candidate;
                }
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("Cannot locate the standalone factory project content root. Keep the source checkout available while running integration tests.");
    }
    public const string IntegrationKey = "test-factory-integration-key-000000000000000000";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(contentRoot);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:TraceabilityConnection"] = connection,
            ["Fscm:BaseUrl"] = "http://fscm.test/", ["Integration:ApiKey"] = IntegrationKey,
            ["Database:SkipAutoMigrate"] = (!migrateDatabase).ToString(),
        }));
        builder.ConfigureServices(services => {
            services.RemoveAll<IFscmGateway>(); services.AddSingleton(gateway);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }
}

internal sealed class TestFactoryFscmGateway(Func<IServiceProvider> warehouse) : IFscmGateway
{
    private async Task<T> Read<T>(Func<AppDbContext, Task<T>> action)
    { await using var scope = warehouse().CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
    public Task<FscmUser> MeAsync(string token, CancellationToken ct) => Read(async db => {
        var user = await db.UserAccountRecords.SingleAsync(x => x.Email == "admin@fscm.local", ct);
        return new FscmUser(user.Id, user.Email, user.FullName, user.Role);
    });
    public async Task<FscmSession> LoginAsync(Fscm.Traceability.Models.LoginRequest request, CancellationToken ct) =>
        new("test-access", DateTimeOffset.UtcNow.AddHours(1), "test-refresh", DateTimeOffset.UtcNow.AddDays(1), await MeAsync("test-access", ct));
    public Task<FscmSession> RefreshAsync(string token, CancellationToken ct) => LoginAsync(new("test", "test"), ct);
    public Task<FscmProduct> ProductAsync(Guid id, string token, CancellationToken ct) => Read(async db => {
        var p = await db.ProductRecords.SingleAsync(x => x.Id == id, ct); return new FscmProduct(p.Id, p.Sku, p.Name, p.Status);
    });
    public Task<FscmWarehouse> WarehouseAsync(Guid id, string token, CancellationToken ct) => Read(async db => {
        var w = await db.WarehouseRecords.SingleAsync(x => x.Id == id, ct); return new FscmWarehouse(w.Id, w.Code, w.Name, w.IsActive);
    });
    public Task<FscmProduct[]> ProductsAsync(string token, CancellationToken ct) => Read(db => db.ProductRecords
        .Select(p => new FscmProduct(p.Id, p.Sku, p.Name, p.Status)).ToArrayAsync(ct));
    public Task<FscmWarehouse[]> WarehousesAsync(string token, CancellationToken ct) => Read(db => db.WarehouseRecords
        .Select(w => new FscmWarehouse(w.Id, w.Code, w.Name, w.IsActive)).ToArrayAsync(ct));
}

internal sealed class FactoryTestClient(HttpClient http, IServiceProvider warehouse)
{
    public async Task<BatchDetail> DetailAsync(Guid id, CancellationToken ct) =>
        (await http.GetFromJsonAsync<BatchDetail>($"/api/batches/{id}", ct))!;
    public async Task<MappingPage> MappingAsync(Guid id, string level, int page, int pageSize, string? pallet = null, string? carton = null, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<MappingPage>($"/api/batches/{id}/mapping?level={level}&page={page}&pageSize={pageSize}" +
            (pallet is null ? "" : "&pallet=" + Uri.EscapeDataString(pallet)) + (carton is null ? "" : "&carton=" + Uri.EscapeDataString(carton)), ct))!;
    public async Task<Guid> SimulateAsync(FSCM.Persistence.WarehouseWork.SimulateProductionRequest request, string key, Guid actor, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/batches/simulate") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        using var response = await http.SendAsync(message, ct); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdResult>(ct))!.Id;
    }
    public async Task<Guid> DispatchAsync(Guid id, Guid destination, CancellationToken ct, Guid[]? palletIds = null)
    {
        using var response = await http.PostAsJsonAsync($"/api/batches/{id}/dispatch", new { destinationWarehouseId = destination, palletIds }, ct);
        response.EnsureSuccessStatusCode(); return id;
    }
    public async Task<FSCM.Persistence.WarehouseWork.FactoryBatchView[]> ListAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<FSCM.Persistence.WarehouseWork.FactoryBatchView[]>("/api/batches", ct) ?? [];
    public async Task<FactoryTraceResult> TraceAsync(string code, Guid actor, string role, CancellationToken ct)
    {
        await using var scope = warehouse.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FactoryTraceabilityService>().TraceAsync(code, actor, role, ct);
    }
    private sealed record IdResult(Guid Id);
}
