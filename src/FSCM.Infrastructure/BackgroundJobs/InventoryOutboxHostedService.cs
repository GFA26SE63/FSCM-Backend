using FSCM.Application.Features.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class InventoryOutboxHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<InventoryOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<InventoryOutboxHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Inventory outbox dispatcher is disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 1, 300));
        var batchSize = Math.Clamp(settings.BatchSize, 1, 500);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<InventoryOutboxDispatcher>();
                var result = await dispatcher.DispatchAsync(batchSize, stoppingToken);
                if (result.MessagesRead > 0)
                {
                    logger.LogInformation(
                        "Inventory outbox dispatch completed: {Read} read, {Processed} processed, {Failed} failed.",
                        result.MessagesRead, result.MessagesProcessed, result.MessagesFailed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Inventory outbox dispatch failed; the polling loop will retry.");
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
