using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Finance.Domain.Receivables;

namespace FSCM.Modules.Finance.Application;

public interface IFinanceUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface IReceivableRepository
{
    Task<Receivable?> GetAsync(string orderCode, CancellationToken ct);
    Task<IReadOnlyList<Receivable>> SearchAsync(string? salesUserId, string? retailerCode, CancellationToken ct);
    /// <summary>Unpaid receivables whose due date is before <paramref name="today"/>.</summary>
    Task<IReadOnlyList<Receivable>> PastDueAsync(DateOnly today, CancellationToken ct);
    void Add(Receivable r);
}
