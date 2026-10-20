using FSCM.Application.Features.Inventory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.BackgroundJobs;

/// <summary>
/// Nightly date-tracker job (BE Sprint 1). Wakes at the configured local time (default 01:30
/// Asia/Ho_Chi_Minh), recomputes batch shelf-life status, and emits <c>BatchExpired</c> outbox
/// messages via <see cref="InventoryExpirySweepService"/>.
/// </summary>
public sealed class DateTrackerHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DateTrackerOptions> options,
    TimeProvider timeProvider,
    ILogger<DateTrackerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Date-tracker job is disabled by configuration.");
            return;
        }

        TimeZoneInfo timeZone;
        TimeSpan runAt;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
            runAt = TimeSpan.ParseExact(settings.RunAtLocalTime, @"hh\:mm", null);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or FormatException)
        {
            logger.LogError(exception,
                "Invalid date-tracker configuration (TimeZone='{TimeZone}', RunAtLocalTime='{RunAt}'); job will not run.",
                settings.TimeZone, settings.RunAtLocalTime);
            return;
        }

        logger.LogInformation(
            "Date-tracker job scheduled daily at {RunAt} ({TimeZone}).", settings.RunAtLocalTime, timeZone.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(timeZone, runAt);
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunSweepAsync(stoppingToken);
        }
    }

    private async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sweep = scope.ServiceProvider.GetRequiredService<InventoryExpirySweepService>();
            var result = await sweep.RunAsync(cancellationToken);
            logger.LogInformation(
                "Date-tracker sweep completed: {Scanned} scanned, {Updated} status updates, {Expired} newly expired.",
                result.BatchesScanned, result.StatusesUpdated, result.BatchesExpired);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown in progress - swallow.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Date-tracker sweep failed; it will retry at the next scheduled run.");
        }
    }

    private TimeSpan GetDelayUntilNextRun(TimeZoneInfo timeZone, TimeSpan runAt)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
        var nextLocal = new DateTimeOffset(nowLocal.Date.Add(runAt), nowLocal.Offset);
        if (nextLocal <= nowLocal) nextLocal = nextLocal.AddDays(1);

        var delay = nextLocal - nowLocal;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
