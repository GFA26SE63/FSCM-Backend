using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.WarehouseConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSCM.Infrastructure.BackgroundJobs;

/// <summary>Checks the live database schedule every 15 seconds; catches up once after a missed run.</summary>
public sealed class DateTrackerHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<DateTrackerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateOnly? lastRunDate = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var settings = await scope.ServiceProvider.GetRequiredService<BackgroundJobSettings>()
                    .ReadAsync<DateTrackerSettings>(BackgroundJobSettings.DateTrackerKey, stoppingToken);
                var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone));
                var date = DateOnly.FromDateTime(local.DateTime);
                if (settings.Enabled && date != lastRunDate &&
                    TimeOnly.FromDateTime(local.DateTime) >= TimeOnly.ParseExact(settings.RunAtLocalTime, "HH:mm"))
                {
                    var result = await scope.ServiceProvider.GetRequiredService<InventoryExpirySweepService>().RunAsync(stoppingToken);
                    lastRunDate = date;
                    logger.LogInformation("Date-tracker: {Scanned} batches scanned, {Expired} expired.",
                        result.BatchesScanned, result.BatchesExpired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Date-tracker failed; retrying on next settings poll."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
