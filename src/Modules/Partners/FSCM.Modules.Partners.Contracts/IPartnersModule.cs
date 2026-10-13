using FSCM.BuildingBlocks.Application;

namespace FSCM.Modules.Partners.Contracts;

public sealed record RetailerDto(string Code, string Name, string Phone, string Address, double Lat, double Lng,
    string AreaCode, string Kind, int CreditTermDays, bool Active, string? PotentialClass);

public sealed record SalesAssignmentDto(string GroupCode, string AreaCode);

/// <summary>Retailer master data, MLOR ratios and the Sales ↔ area assignment, for Ordering / Inventory / Loyalty.</summary>
public interface IPartnersModule
{
    Task<RetailerDto?> GetRetailerAsync(string code, CancellationToken ct = default);
    /// <summary>Retailer-specific m for the SKU (MLOR = m × SL); null → use the product default.</summary>
    Task<decimal?> MlorRatioAsync(string retailerCode, string sku, CancellationToken ct = default);
    Task<SalesAssignmentDto?> AssignmentOfSalesAsync(string salesUserId, CancellationToken ct = default);
    /// <summary>Out-of-area guard: a Sales may only order for retailers in the area of their sales group.</summary>
    Task<bool> IsInSalesAreaAsync(string salesUserId, string retailerCode, CancellationToken ct = default);
    Task<IReadOnlyList<RetailerDto>> RetailersOfSalesAsync(string salesUserId, DateTime? changedSinceUtc, CancellationToken ct = default);
}

public sealed record RetailerRegistered(string RequestCode, string RetailerCode, string Name, string AreaCode, string DeclaredByUserId) : IntegrationEvent;
public sealed record RetailerRegistrationRejected(string RequestCode, string Name, string DeclaredByUserId, string Reason) : IntegrationEvent;
public sealed record SalesGroupMembersChanged(string GroupCode, string AreaCode, IReadOnlyList<string> MemberUserIds, IReadOnlyList<string> RemovedUserIds) : IntegrationEvent;
