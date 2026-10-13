using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Partners.Domain.Areas;
using FSCM.Modules.Partners.Domain.Retailers;

namespace FSCM.Modules.Partners.Application;

public interface IPartnersUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface IAreaRepository
{
    Task<Area?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Area>> AllAsync(CancellationToken ct);
    void Add(Area a);
    Task<SalesGroup?> GroupAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<SalesGroup>> GroupsAsync(string? areaCode, CancellationToken ct);
    Task<SalesGroup?> GroupOfMemberAsync(string userId, CancellationToken ct);
    void Add(SalesGroup g);
}

public interface IRetailerRepository
{
    Task<Retailer?> GetAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<Retailer>> SearchAsync(string? search, string? areaCode, DateTime? changedSinceUtc, CancellationToken ct);
    void Add(Retailer r);
    Task<RetailerRegistration?> RegistrationAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<RetailerRegistration>> RegistrationsAsync(RegistrationStatus? status, string? declaredBy, CancellationToken ct);
    void Add(RetailerRegistration r);
}
