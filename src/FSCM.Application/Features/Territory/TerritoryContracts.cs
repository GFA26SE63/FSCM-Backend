namespace FSCM.Application.Features.Territory;

public sealed record RegionResult(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SalesGroupResult(
    Guid Id,
    Guid RegionId,
    string RegionCode,
    string RegionName,
    string Code,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SalesGroupMembershipResult(
    Guid Id,
    Guid SalesGroupId,
    string SalesGroupCode,
    Guid UserId,
    string UserEmail,
    string UserFullName,
    DateTimeOffset AssignedAt,
    DateTimeOffset? UnassignedAt,
    bool IsActive);

public sealed record RetailerTerritoryAssignmentResult(
    Guid Id,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    Guid SalesGroupId,
    string SalesGroupCode,
    DateTimeOffset AssignedAt,
    DateTimeOffset? UnassignedAt,
    bool IsActive);

public sealed record TerritoryOrderAccessResult(
    Guid UserId,
    Guid RetailerId,
    bool CanCreateOrder,
    Guid? SalesGroupId,
    string? SalesGroupCode,
    string? DenialReason);
