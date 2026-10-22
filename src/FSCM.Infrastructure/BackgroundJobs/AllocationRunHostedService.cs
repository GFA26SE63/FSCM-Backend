using FSCM.Application.Features.AllocationRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.BackgroundJobs;

/// <summary>
/// The daily allocation run (F85). Wakes at the configured cutoff, default 12:00
/// Asia/Ho_Chi_Minh, closes the run and builds the plan.
///
/// Closing is idempotent on the business day, so a restart, a second instance or a late wake-up
/// joins the run that already exists instead of splitting one day's orders across two plans. A
/// failure is logged and left for the next cutoff: the run is a proposal, and quietly retrying a
/// half-built plan would be worse than not having one.
/// </summary>
public sealed class AllocationRunHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AllocationRunOptions> options,
    TimeProvider timeProvider,
    ILogger<AllocationRunHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Allocation run job is disabled by configuration.");
            return;
        }

        TimeZoneInfo timeZone;
        TimeSpan cutoff;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
            cutoff = TimeSpan.ParseExact(settings.CutoffLocalTime, @"hh\:mm", null);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or FormatException)
        {
            logger.LogError(exception,
                "Invalid allocation run configuration (TimeZone='{TimeZone}', CutoffLocalTime='{Cutoff}'); job will not run.",
                settings.TimeZone, settings.CutoffLocalTime);
            return;
        }

        logger.LogInformation(
            "Allocation run scheduled daily at {Cutoff} ({TimeZone}).", settings.CutoffLocalTime, timeZone.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextCutoff(timeZone, cutoff);
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunAsync(timeZone, cutoff, stoppingToken);
        }
    }

    private async Task RunAsync(TimeZoneInfo timeZone, TimeSpan cutoff, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AllocationRunService>();

            // The cutoff instant, not "now": waking a few seconds late must not shift which orders
            // qualify, otherwise the same day could include different orders on a retry.
            var nowLocal = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
            var cutoffAt = new DateTimeOffset(nowLocal.Date.Add(cutoff), nowLocal.Offset);

            var run = await service.StartAsync(cutoffAt, cancellationToken);
            logger.LogInformation(
                "Allocation run {RunCode} closed for {BusinessDate} with {OrderCount} orders.",
                run.RunCode, run.BusinessDate, run.OrderCount);

            if (run.Status != AllocationRunStatuses.Snapshotted)
            {
                logger.LogInformation(
                    "Allocation run {RunCode} is already {Status}; leaving its plan untouched.",
                    run.RunCode, run.Status);
                return;
            }

            var pricing = await service.PlanAsync(run.Id, cancellationToken);
            logger.LogInformation(
                "Allocation run {RunCode} planned: {FulfilledCount} orders priced and awaiting approval.",
                run.RunCode, pricing.Orders.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown in progress - swallow.
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Allocation run failed; it will be retried at the next cutoff rather than rebuilt now.");
        }
    }

    private TimeSpan GetDelayUntilNextCutoff(TimeZoneInfo timeZone, TimeSpan cutoff)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
        var nextLocal = new DateTimeOffset(nowLocal.Date.Add(cutoff), nowLocal.Offset);
        if (nextLocal <= nowLocal) nextLocal = nextLocal.AddDays(1);

        var delay = nextLocal - nowLocal;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
