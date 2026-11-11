using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Allocation;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class RealTimeRecoveryTests
{
    [Theory]
    [InlineData(true, 3, OrderStatuses.AllocationConflict)]
    [InlineData(false, 1, OrderStatuses.AllocationFailed)]
    public async Task Exhausted_or_technical_failures_leave_a_retryable_status(bool conflict, int attempts, string status)
    {
        var store = new FailingStore(conflict);
        var service = new RealTimeAllocationService(store, null!, TimeProvider.System, null!);
        await service.AllocateAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Equal(attempts, store.Attempts);
        Assert.Equal(status, store.FailedStatus);
    }

    private sealed class FailingStore(bool conflict) : IRealTimeAllocationStore
    {
        public int Attempts;
        public string? FailedStatus;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
        { Attempts++; throw conflict ? new AllocationConcurrencyException("conflict") : new InvalidOperationException("interrupted"); }
        public Task MarkFailedAsync(Guid order, Guid actor, string status, string reason, DateTimeOffset now, CancellationToken ct)
        { FailedStatus = status; return Task.CompletedTask; }
        public Task<RealTimeAllocationContext?> LoadForUpdateAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<(string OrderCode, string Status)> ReadOrderStateAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public void AddRun(AllocationRun x) { }
        public void AddAllocation(FSCM.Domain.Entities.Allocation x) { }
        public void AddReservation(Reservation x) { }
        public void AddTransaction(InventoryTransaction x) { }
        public void AddQuote(SalesOrderQuote x) { }
        public void AddHistory(OrderStatusHistory x) { }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public void ClearTracking() { }
    }
}
