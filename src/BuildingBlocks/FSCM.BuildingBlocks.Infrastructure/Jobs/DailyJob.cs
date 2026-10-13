using FSCM.BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSCM.BuildingBlocks.Infrastructure.Jobs;

/// <summary>Runs every registered IScheduledJob once a day at its RunAt time (can be replaced by Azure Functions Timer Trigger).</summary>
public sealed class DailyJobScheduler(IServiceScopeFactory scopes, ILogger<DailyJobScheduler> log) : BackgroundService
{
    private static readonly TimeZoneInfo Vn = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private readonly Dictionary<string, DateOnly> _lastRun = new();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Vn);
            var today = DateOnly.FromDateTime(now);
            using (var probe = scopes.CreateScope())
            {
                var due = probe.ServiceProvider.GetServices<IScheduledJob>().OrderBy(j => j.RunAt)
                    .Where(j => TimeOnly.FromDateTime(now) >= j.RunAt && !(_lastRun.TryGetValue(j.Name, out var d) && d == today))
                    .Select(j => j.Name).ToList();
                foreach (var name in due)
                {
                    using var scope = scopes.CreateScope(); // fresh DbContexts per job
                    var job = scope.ServiceProvider.GetServices<IScheduledJob>().First(j => j.Name == name);
                    try { await job.RunAsync(stop); _lastRun[name] = today; log.LogInformation("Job {Job} done", name); }
                    catch (Exception ex) { log.LogError(ex, "Job {Job} failed", name); }
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
        }
    }
}
