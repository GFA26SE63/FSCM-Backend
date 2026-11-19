using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Allocation;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class RealTimeRecoveryTests
{
    [Fact]
    [Trait("Report5Case", "UT-Allocation Run Result-UTCID03")]
    public async Task Three_version_collisions_create_an_allocation_conflict_retry_record_not_out_of_stock()
    {
        var orderId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var store = new FailingStore(conflict: true);
        var service = new RealTimeAllocationService(store, null!, TimeProvider.System, null!);

        await service.AllocateAsync(orderId, actorId, default);

        Assert.Equal(3, store.Attempts);
        Assert.Equal(3, store.ClearTrackingCalls);
        Assert.Equal(1, store.MarkFailedCalls);
        Assert.Equal(orderId, store.FailedOrderId);
        Assert.Equal(actorId, store.FailedActorId);
        Assert.Equal(OrderStatuses.AllocationConflict, store.FailedStatus);
        Assert.NotEqual(OrderStatuses.OutOfStock, store.FailedStatus);
        Assert.Contains("3 lần thử", store.FailureReason);
    }

    [Fact]
    // Report5 UT-Allocation Plan Approval-UTCID03: ordinary technical failures remain retryable;
    // authorization and reallocation from current snapshots are covered by RealTimePostgresWorkflowTests.
    public async Task Ordinary_technical_failure_leaves_an_allocation_failed_status_after_one_attempt()
    {
        var store = new FailingStore(conflict: false);
        var service = new RealTimeAllocationService(store, null!, TimeProvider.System, null!);

        await service.AllocateAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        Assert.Equal(1, store.Attempts);
        Assert.Equal(1, store.MarkFailedCalls);
        Assert.Equal(OrderStatuses.AllocationFailed, store.FailedStatus);
    }

    private sealed class FailingStore(bool conflict) : IRealTimeAllocationStore
    {
        public int Attempts;
        public int ClearTrackingCalls;
        public int MarkFailedCalls;
        public Guid FailedOrderId;
        public Guid FailedActorId;
        public string? FailedStatus;
        public string? FailureReason;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
        { Attempts++; throw conflict ? new AllocationConcurrencyException("conflict") : new InvalidOperationException("interrupted"); }
        public Task MarkFailedAsync(Guid order, Guid actor, string status, string reason, DateTimeOffset now, CancellationToken ct)
        {
            MarkFailedCalls++;
            FailedOrderId = order;
            FailedActorId = actor;
            FailedStatus = status;
            FailureReason = reason;
            return Task.CompletedTask;
        }
        public Task<RealTimeAllocationContext?> LoadForUpdateAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<(string OrderCode, string Status)> ReadOrderStateAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public void AddRun(AllocationRun x) { }
        public void AddAllocation(FSCM.Domain.Entities.Allocation x) { }
        public void AddReservation(Reservation x) { }
        public void AddTransaction(InventoryTransaction x) { }
        public void AddQuote(SalesOrderQuote x) { }
        public void AddHistory(OrderStatusHistory x) { }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public void ClearTracking() => ClearTrackingCalls++;
    }
}
