using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

/// <summary>At-least-once delivery; the factory acknowledgement is idempotent by pallet/receipt.</summary>
public sealed class FactoryReceiptSyncDispatcher(AppDbContext db, FactoryTraceabilityService factory, TimeProvider clock)
{
    public async Task<int> DispatchAsync(CancellationToken ct)
    {
        var pending = await db.Set<FactoryReceiptSync>().Where(x => x.SyncedAt == null).OrderBy(x => x.ReceivedAt).ThenBy(x => x.PalletId).Take(100).ToArrayAsync(ct);
        var synced = 0;
        foreach (var message in pending)
        {
            try
            {
                await factory.AcknowledgeReceiptAsync(message, ct);
                message.SyncedAt = clock.GetUtcNow(); message.LastError = null; synced++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { message.LastError = e.GetType().Name; }
            message.Attempts++;
            await db.SaveChangesAsync(ct);
        }
        return synced;
    }
}
