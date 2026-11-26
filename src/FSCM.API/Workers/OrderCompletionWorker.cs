using FSCM.Application.Features.Orders;

namespace FSCM.API.Workers;

/// <summary>
/// Periodically re-evaluates Delivered orders so orders whose admin-configured complaint grace
/// period has elapsed move to Completed and earn loyalty points automatically.
/// </summary>
public sealed class OrderCompletionWorker(
    IServiceScopeFactory scopes,
    ILogger<OrderCompletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IOrderCompletionStore>();
                var completion = scope.ServiceProvider.GetRequiredService<OrderCompletionService>();
                var orderIds = await store.ListDeliveredOrderIdsAsync(stoppingToken);
                foreach (var orderId in orderIds)
                {
                    await completion.EvaluateAsync(orderId, null, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                logger.LogWarning(e, "Order completion sweep failed; retrying on next tick.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
