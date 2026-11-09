using FSCM.Application.Features.Quotes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSCM.Infrastructure.BackgroundJobs;

public sealed class QuoteExpirationHostedService(IServiceScopeFactory scopes, ILogger<QuoteExpirationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<QuoteService>().ExpirePendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Quote expiration failed; retrying on the next sweep."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
