using FSCM.Application.Features.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class OrderDeadlineHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OrderDeadlineOptions> options,
    TimeProvider timeProvider,
    ILogger<OrderDeadlineHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        var interval = TimeSpan.FromMinutes(Math.Clamp(settings.PollIntervalMinutes, 1, 60));
        var windowDays = Math.Clamp(settings.PrepaidPaymentWindowDays, 1, 30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<OrderDeadlineService>();
                var result = await service.SweepAsync(windowDays, stoppingToken);
                if (result.ExpiredQuotes + result.ExpiredPrepaidPayments + result.PaymentReminders > 0)
                {
                    logger.LogInformation(
                        "Order deadline sweep: {Quotes} quotes expired, {Payments} prepaid orders cancelled, {Reminders} reminders sent.",
                        result.ExpiredQuotes, result.ExpiredPrepaidPayments, result.PaymentReminders);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Order deadline sweep failed; it will retry on the next interval.");
            }

            try { await Task.Delay(interval, timeProvider, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
