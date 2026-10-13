namespace FSCM.Modules.Identity.Contracts;

public sealed record UserDto(string Id, string FullName, string Email, string Phone, string Role,
    string? WarehouseCode, string? SalesGroupCode, string? RetailerCode, bool Active);

public sealed record DeviceDto(string DeviceId, string Platform, string PushToken);

/// <summary>Who is who, for Notifications (recipients, push tokens) and for any module that needs a user's display name.</summary>
public interface IIdentityModule
{
    Task<UserDto?> GetUserAsync(string userId, CancellationToken ct = default);
    /// <summary>Active users of a role (Admin, Operator, WarehouseKeeper…), optionally limited to one warehouse.</summary>
    Task<IReadOnlyList<UserDto>> UsersInRoleAsync(string role, string? warehouseCode = null, CancellationToken ct = default);
    Task<IReadOnlyList<UserDto>> UsersOfRetailerAsync(string retailerCode, CancellationToken ct = default);
    Task<IReadOnlyList<DeviceDto>> DevicesOfAsync(string userId, CancellationToken ct = default);
}
