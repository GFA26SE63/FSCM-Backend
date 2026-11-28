using FSCM.Application.Features.AllocationRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FSCM.Application.Features.WarehouseConfiguration;

namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class AllocationRecoveryHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<AllocationRecoveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset? lastRun = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var settings = await scope.ServiceProvider.GetRequiredService<BackgroundJobSettings>()
                    .ReadAsync<AllocationRecoverySettings>(BackgroundJobSettings.AllocationRecoveryKey, stoppingToken);
                if (settings.Enabled && (lastRun is null || clock.GetUtcNow() >= lastRun.Value.AddMinutes(1)))
                {
                    var store = scope.ServiceProvider.GetRequiredService<IRealTimeAllocationStore>();
                    var pending = await store.ListRecoverableAsync(clock.GetUtcNow().AddMinutes(-2), stoppingToken);
                    foreach (var order in pending)
                    {
                        await using var attempt = scopes.CreateAsyncScope();
                        await attempt.ServiceProvider.GetRequiredService<IRealTimeAllocationService>()
                            .AllocateAsync(order.Id, order.Actor, stoppingToken);
                    }
                    lastRun = clock.GetUtcNow();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Allocation recovery failed; next sweep will retry."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
