using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.WarehouseConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class InventoryOutboxHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<InventoryOutboxHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset? lastRun = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delaySeconds = 15;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var settings = await scope.ServiceProvider.GetRequiredService<BackgroundJobSettings>()
                    .ReadAsync<InventoryOutboxSettings>(BackgroundJobSettings.InventoryOutboxKey, stoppingToken);
                delaySeconds = Math.Min(settings.PollIntervalSeconds, 15);
                if (settings.Enabled && (lastRun is null ||
                    clock.GetUtcNow() >= lastRun.Value.AddSeconds(settings.PollIntervalSeconds)))
                {
                    await scope.ServiceProvider.GetRequiredService<InventoryOutboxDispatcher>()
                        .DispatchAsync(settings.BatchSize, stoppingToken);
                    lastRun = clock.GetUtcNow();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Inventory outbox failed; retrying on next settings poll."); }
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
