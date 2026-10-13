using FSCM.Modules.Pricing.Application;
using FSCM.Modules.Pricing.Domain.BatchDiscounts;
using FSCM.Modules.Pricing.Domain.Promotions;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Modules.Pricing.Infrastructure;

internal sealed class PromotionRepository(PricingDbContext db) : IPromotionRepository
{
    public Task<Promotion?> GetAsync(string code, CancellationToken ct) => db.Promotions.FirstOrDefaultAsync(p => p.Id == code, ct);
    public async Task<IReadOnlyList<Promotion>> AllAsync(CancellationToken ct) => await db.Promotions.OrderBy(p => p.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<Promotion>> ActiveOnAsync(DateOnly date, CancellationToken ct) =>
        await db.Promotions.Where(p => p.Status == PromotionStatus.Active && p.ValidFrom <= date && p.ValidTo >= date).ToListAsync(ct);
    public void Add(Promotion p) => db.Promotions.Add(p);
}
internal sealed class BatchDiscountRepository(PricingDbContext db) : IBatchDiscountRepository
{
    public Task<BatchDiscount?> GetAsync(string code, CancellationToken ct) => db.BatchDiscounts.FirstOrDefaultAsync(d => d.Id == code, ct);
    public async Task<IReadOnlyList<BatchDiscount>> AllAsync(CancellationToken ct) => await db.BatchDiscounts.OrderByDescending(d => d.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<BatchDiscount>> ActiveOnAsync(DateOnly date, CancellationToken ct) =>
        await db.BatchDiscounts.Where(d => d.Status == BatchDiscountStatus.Active && d.From <= date && d.To >= date).ToListAsync(ct);
    public void Add(BatchDiscount d) => db.BatchDiscounts.Add(d);
}
