using FSCM.Application.Features.Orders;
using FSCM.Application.Features.WarehouseConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class OrderDeadlineHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<OrderDeadlineHostedService> logger) : BackgroundService
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
                    .ReadAsync<OrderDeadlineSettings>(BackgroundJobSettings.OrderDeadlineKey, stoppingToken);
                if (settings.Enabled && (lastRun is null ||
                    clock.GetUtcNow() >= lastRun.Value.AddMinutes(settings.PollIntervalMinutes)))
                {
                    var result = await scope.ServiceProvider.GetRequiredService<OrderDeadlineService>()
                        .SweepAsync(settings.PrepaidPaymentWindowDays, stoppingToken);
                    lastRun = clock.GetUtcNow();
                    if (result.ExpiredQuotes + result.ExpiredPrepaidPayments + result.PaymentReminders > 0)
                        logger.LogInformation("Order deadlines: {Quotes} quotes expired, {Payments} prepaid orders cancelled, {Reminders} reminders.",
                            result.ExpiredQuotes, result.ExpiredPrepaidPayments, result.PaymentReminders);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Order deadline sweep failed; retrying on next settings poll."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
