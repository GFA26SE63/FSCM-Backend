using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.DeliveryEta;
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
///
/// The cutoff comes from the published run-day schedule, the same one the ETA is built from. It is
/// re-read before every wait, so changing it takes effect the next day without a restart. The
/// configured time is only a fallback for a deployment where no schedule has been published yet:
/// two independent cutoffs would let the ETA promise one time and the job keep another.
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

        TimeZoneInfo fallbackZone;
        TimeSpan fallbackCutoff;
        try
        {
            fallbackZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
            fallbackCutoff = TimeSpan.ParseExact(settings.CutoffLocalTime, @"hh\:mm", null);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or FormatException)
        {
            logger.LogError(exception,
                "Invalid allocation run fallback configuration (TimeZone='{TimeZone}', CutoffLocalTime='{Cutoff}'); job will not run.",
                settings.TimeZone, settings.CutoffLocalTime);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var (timeZone, cutoff) = await ResolveCutoffAsync(fallbackZone, fallbackCutoff, stoppingToken);
            var delay = GetDelayUntilNextCutoff(timeZone, cutoff);
            logger.LogInformation(
                "Next allocation run at {Cutoff} ({TimeZone}), in {Delay}.", cutoff, timeZone.Id, delay);

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

    /// <summary>
    /// The cutoff the Admin published, or the configured one if no schedule exists yet. A schedule
    /// that cannot be read is reported and the fallback used: a run at the wrong hour is recoverable,
    /// a day with no run at all is not.
    /// </summary>
    private async Task<(TimeZoneInfo Zone, TimeSpan Cutoff)> ResolveCutoffAsync(
        TimeZoneInfo fallbackZone, TimeSpan fallbackCutoff, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var eta = scope.ServiceProvider.GetRequiredService<DeliveryEtaService>();
            var schedule = await eta.GetScheduleAsync(null, cancellationToken);
            var cutoff = schedule.Milestones.Single(item => item.Key == "allocation_run").LocalTime;
            return (TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone), cutoff.ToTimeSpan());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not read the published run schedule; falling back to {Cutoff} ({TimeZone}).",
                fallbackCutoff, fallbackZone.Id);
            return (fallbackZone, fallbackCutoff);
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
