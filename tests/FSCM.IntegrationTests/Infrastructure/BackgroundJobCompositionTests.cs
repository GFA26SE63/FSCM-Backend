using System.Reflection;
using System.Text.Json;
using FSCM.Application;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.Infrastructure.BackgroundJobs;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Inventory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FSCM.IntegrationTests.Infrastructure;

public sealed class BackgroundJobCompositionTests
{
    private static ServiceCollection ProductionServices()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = new string('s', 64),
            ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging().AddSingleton<IConfiguration>(config);
        // Dependency composition only: no database provider, connection or data provisioning.
        services.AddDbContext<AppDbContext>();
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        return services;
    }

    [Fact]
    public async Task Production_registrations_resolve_all_background_job_dependencies()
    {
        await using var provider = ProductionServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Assert.IsType<InventoryStore>(services.GetRequiredService<IBatchExpiryStore>());
        Assert.NotNull(services.GetRequiredService<InventoryExpirySweepService>());
        Assert.NotNull(services.GetRequiredService<InventoryOutboxDispatcher>());
        Assert.NotNull(services.GetRequiredService<OrderDeadlineService>());
        Assert.NotNull(services.GetRequiredService<IRealTimeAllocationService>());
        Assert.NotNull(services.GetRequiredService<BackgroundJobSettings>());
        var workers = provider.GetServices<IHostedService>().ToArray();
        Assert.Contains(workers, w => w is DateTrackerHostedService);
        Assert.Contains(workers, w => w is InventoryOutboxHostedService);
        Assert.Contains(workers, w => w is OrderDeadlineHostedService);
        Assert.Contains(workers, w => w is AllocationRecoveryHostedService);
    }

    [Fact]
    public async Task Date_tracker_completes_due_sweep_using_registered_application_service()
    {
        var expiry = new EmptyExpiryStore();
        var settings = DispatchProxy.Create<IWarehouseConfigurationStore, SettingsStore>();
        var services = ProductionServices();
        services.RemoveAll<IBatchExpiryStore>();
        services.AddSingleton<IBatchExpiryStore>(expiry);
        services.RemoveAll<IWarehouseConfigurationStore>();
        services.AddSingleton(settings);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new NoonClock());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var worker = Assert.Single(provider.GetServices<IHostedService>().OfType<DateTrackerHostedService>());
        try
        {
            await worker.StartAsync(default);
            var run = await expiry.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ExpirySweepStatuses.Completed, run.Status);
            Assert.Equal(new DateOnly(2026, 11, 28), run.BusinessDate);
            Assert.Equal(0, run.BatchesScanned);
        }
        finally { await worker.StopAsync(default); }
    }

    public class SettingsStore : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IWarehouseConfigurationStore.FindCurrentConfigurationAsync)
                ? Task.FromResult<SystemConfiguration?>(new SystemConfiguration
                {
                    ConfigKey = BackgroundJobSettings.DateTrackerKey,
                    ConfigValue = JsonSerializer.Serialize(new { enabled = true, timeZone = "Asia/Ho_Chi_Minh", runAtLocalTime = "01:30" }),
                    IsActive = true
                })
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class NoonClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 28, 5, 0, 0, TimeSpan.Zero);
    }

    private sealed class EmptyExpiryStore : IBatchExpiryStore
    {
        private ExpirySweepRun? run;
        public TaskCompletionSource<ExpirySweepRun> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyCollection<BatchExpiryCandidate>> ListBatchesForExpirySweepAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<BatchExpiryCandidate>>([]);
        public void AddSweepRun(ExpirySweepRun value) => run = value;
        public void AddOutboxMessage(OutboxMessage message) => throw new InvalidOperationException("Empty sweep cannot emit events.");
        public void AddInventoryLock(InventoryLock value) => throw new InvalidOperationException("Empty sweep cannot lock inventory.");
        public Task SaveChangesAsync(CancellationToken ct)
        {
            if (run?.Status == ExpirySweepStatuses.Completed) Completed.TrySetResult(run);
            return Task.CompletedTask;
        }
    }
}
