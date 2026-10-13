using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Loyalty.Domain.Gifts;
using FSCM.Modules.Loyalty.Domain.Points;
using FSCM.Modules.Loyalty.Domain.Programs;

namespace FSCM.Modules.Loyalty.Application;

public interface ILoyaltyUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface IProgramRepository
{
    Task<LoyaltyProgram?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<LoyaltyProgram>> AllAsync(CancellationToken ct);
    void Add(LoyaltyProgram p);
}
public interface IPointRepository
{
    Task<PointAccount?> GetAccountAsync(string retailerCode, CancellationToken ct);
    void Add(PointAccount a);
    void Add(PointLedgerEntry e);
    Task<PointLedgerEntry?> EntryForOrderAsync(string retailerCode, string orderCode, CancellationToken ct);
    Task<IReadOnlyList<PointLedgerEntry>> LedgerAsync(string retailerCode, CancellationToken ct);
}
public interface IGiftRepository
{
    Task<Gift?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Gift>> AllAsync(CancellationToken ct);
    void Add(Gift g);
    Task<GiftRedemption?> RedemptionAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<GiftRedemption>> RedemptionsAsync(string? retailerCode, RedemptionStatus? status, CancellationToken ct);
    void Add(GiftRedemption r);
}
