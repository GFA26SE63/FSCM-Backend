using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Identity.Domain.Users;

namespace FSCM.Modules.Identity.Application;

public interface IIdentityUnitOfWork : IUnitOfWork, IEventOutbox { }

public interface IUserRepository
{
    Task<AppUser?> GetAsync(string id, CancellationToken ct);
    /// <summary>Resolves a token subject: Entra object id first, then the internal id (dev / seeded accounts).</summary>
    Task<AppUser?> ResolveAsync(string subject, CancellationToken ct);
    Task<IReadOnlyList<AppUser>> SearchAsync(string? search, UserRole? role, CancellationToken ct);
    Task<IReadOnlyList<AppUser>> InRoleAsync(UserRole role, string? warehouseCode, CancellationToken ct);
    Task<IReadOnlyList<AppUser>> OfRetailerAsync(string retailerCode, CancellationToken ct);
    Task<IReadOnlyList<AppUser>> ByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct);
    void Add(AppUser u);
}
