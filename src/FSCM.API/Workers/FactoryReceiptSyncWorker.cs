using FSCM.Persistence.WarehouseWork;

namespace FSCM.API.Workers;

public sealed class FactoryReceiptSyncWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<FactoryReceiptSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(config["Traceability:ApiKey"])) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FactoryReceiptSyncDispatcher>().DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Factory receipt acknowledgement pending ({ErrorType}); retrying.", e.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
