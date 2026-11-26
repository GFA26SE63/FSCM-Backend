namespace FSCM.Application.Features.Roles;

public sealed record RoleSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string BaseRole = "Operator");

public sealed record RoleDetailsDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string BaseRole = "Operator");

public sealed record CreateRoleRequest(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string>? Permissions,
    string BaseRole = "Operator");

public sealed record UpdateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<string>? Permissions);

public sealed record ScreenFeatureDto(
    string Key,
    string Name,
    string Description,
    string GroupKey,
    string GroupLabel);
