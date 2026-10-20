using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Identity;

public interface IIdentityStore
{
    Task<UserAccount?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<UserAccount?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindUserByEntraObjectIdAsync(string entraObjectId, CancellationToken cancellationToken);

    Task<(IReadOnlyCollection<UserAccount> Items, int TotalCount)> ListUsersAsync(
        UserListQuery query,
        CancellationToken cancellationToken);

    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);

    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    Task<AccountToken?> FindAccountTokenAsync(
        string tokenHash,
        string purpose,
        CancellationToken cancellationToken);

    Task<IdentityScope> GetScopeAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken);

    Task<bool> SalesGroupExistsAsync(Guid salesGroupId, CancellationToken cancellationToken);

    Task<bool> RegionExistsAsync(Guid regionId, CancellationToken cancellationToken);

    Task<Retailer?> FindRetailerAsync(Guid retailerId, CancellationToken cancellationToken);

    Task<Retailer?> FindRetailerByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserWarehouseAssignment?> FindWarehouseAssignmentAsync(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken);

    Task<SalesGroupMember?> FindSalesGroupMembershipAsync(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SalesGroupMember>> ListActiveSalesGroupMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<UserRegionAssignment?> FindRegionAssignmentAsync(
        Guid userId,
        Guid regionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RefreshToken>> GetActiveRefreshTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    void AddUser(UserAccount user);

    void AddCredential(UserCredential credential);

    void AddRefreshToken(RefreshToken refreshToken);

    void AddAccountToken(AccountToken accountToken);

    void AddWarehouseAssignment(UserWarehouseAssignment assignment);

    void AddSalesGroupMembership(SalesGroupMember membership);

    void AddRegionAssignment(UserRegionAssignment assignment);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

