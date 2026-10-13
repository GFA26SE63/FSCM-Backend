using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Pricing.Domain.BatchDiscounts;
using FSCM.Modules.Pricing.Domain.Promotions;

namespace FSCM.Modules.Pricing.Application;

public interface IPricingUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface IPromotionRepository
{
    Task<Promotion?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken ct);
    Task<IReadOnlyList<Promotion>> ActiveOnAsync(DateOnly date, CancellationToken ct);
    void Add(Promotion p);
}

public interface IBatchDiscountRepository
{
    Task<BatchDiscount?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<BatchDiscount>> AllAsync(CancellationToken ct);
    Task<IReadOnlyList<BatchDiscount>> ActiveOnAsync(DateOnly date, CancellationToken ct);
    void Add(BatchDiscount d);
}
