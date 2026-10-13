using System.Text.Json;
using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FSCM.BuildingBlocks.Infrastructure.Events;

/// <summary>Resolves every IIntegrationEventHandler&lt;T&gt; registered by the modules and runs them in-process (one failing handler does not stop the others; the exception is rethrown at the end so the outbox retries).</summary>
public sealed class InProcessEventBus(IServiceProvider sp) : IEventBus
{
    public async Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IIntegrationEvent
    {
        List<Exception>? failures = null;
        foreach (var h in sp.GetServices<IIntegrationEventHandler<T>>())
        {
            try { await h.HandleAsync(@event, ct); }
            catch (Exception ex) { (failures ??= new()).Add(ex); }
        }
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }
}

/// <summary>
/// Worker loop of the transactional outbox. Every module wrote its integration events into its own outbox table in the same transaction as
/// the business change; here they are read oldest first, published to the handlers and marked processed. Each message runs in its own DI scope
/// (fresh DbContexts), so a failing handler cannot poison the next message; failures increase Attempts (max 5) and keep LastError.
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> log) : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await DispatchOnceAsync(stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Outbox dispatch loop failed; will retry"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> DispatchOnceAsync(CancellationToken stop)
    {
        var pending = new List<(Type Context, Guid Id)>();
        using (var scope = scopes.CreateScope())
            foreach (var db in scope.ServiceProvider.GetServices<ModuleDbContext>())
            {
                var ids = await db.Outbox.Where(x => x.ProcessedAtUtc == null && x.Attempts < MaxAttempts).OrderBy(x => x.OccurredAtUtc).Take(BatchSize).Select(x => x.Id).ToListAsync(stop);
                pending.AddRange(ids.Select(id => (db.GetType(), id)));
            }

        foreach (var (contextType, id) in pending)
        {
            string? error;
            using (var scope = scopes.CreateScope())
            {
                var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType);
                var m = await db.Outbox.FirstOrDefaultAsync(x => x.Id == id && x.ProcessedAtUtc == null, stop);
                if (m is null) continue;
                try
                {
                    var type = Type.GetType(m.Type, throwOnError: true)!;
                    var evt = JsonSerializer.Deserialize(m.Payload, type)!;
                    var publish = typeof(IEventBus).GetMethod(nameof(IEventBus.PublishAsync))!.MakeGenericMethod(type);
                    await (Task)publish.Invoke(scope.ServiceProvider.GetRequiredService<IEventBus>(), new[] { evt, stop })!;
                    m.ProcessedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(stop);
                    continue;
                }
                catch (Exception ex) { error = (ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex).Message; log.LogWarning(ex, "Outbox {Id} failed", id); }
            }
            using var failScope = scopes.CreateScope();
            var failDb = (ModuleDbContext)failScope.ServiceProvider.GetRequiredService(contextType);
            var failed = await failDb.Outbox.FirstAsync(x => x.Id == id, stop);
            failed.Attempts++; failed.LastError = error;
            await failDb.SaveChangesAsync(stop);
        }
        return pending.Count;
    }
}
