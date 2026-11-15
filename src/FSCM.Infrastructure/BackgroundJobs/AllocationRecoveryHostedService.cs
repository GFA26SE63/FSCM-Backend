using FSCM.Application.Features.AllocationRuns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class AllocationRecoveryHostedService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<AllocationRecoveryHostedService> logger, IOptions<AllocationRecoveryOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IRealTimeAllocationStore>();
                var pending = await store.ListRecoverableAsync(clock.GetUtcNow().AddMinutes(-2), stoppingToken);
                foreach (var order in pending)
                {
                    await using var attempt = scopes.CreateAsyncScope();
                    await attempt.ServiceProvider.GetRequiredService<IRealTimeAllocationService>()
                        .AllocateAsync(order.Id, order.Actor, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Allocation recovery failed; next sweep will retry."); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

public sealed class AllocationRecoveryOptions
{
    public bool Enabled { get; set; } = true;
}
