using FSCM.BuildingBlocks.Application;
using FSCM.Modules.Identity.Contracts;
using FSCM.Modules.Identity.Domain.Users;
using FSCM.Modules.Partners.Contracts;
using FSCM.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.Modules.Identity.Application;

public sealed record CreateUserInput(string FullName, string Email, string Phone, string Role, string? EntraObjectId, string? WarehouseCode, string? AreaCode, string? RetailerCode);
public sealed record UpdateUserInput(string FullName, string Email, string Phone, bool Active, string? WarehouseCode, string? AreaCode, string? RetailerCode);
public sealed record RegisterDeviceInput(string DeviceId, string Platform, string PushToken);

/// <summary>Màn "Người dùng": Admin creates every role except Admin.</summary>
public sealed class UserService(IUserRepository users, IIdentityUnitOfWork uow, IClock clock)
{
    public async Task<PagedResult<UserDto>> SearchAsync(PageRequest page, string? role, CancellationToken ct)
    {
        UserRole? r = Enum.TryParse<UserRole>(role, true, out var parsed) ? parsed : null;
        return (await users.SearchAsync(page.Search, r, ct)).Select(IdentityModule.Map).Page(page);
    }

    public async Task<UserDto> GetAsync(string id, CancellationToken ct) => IdentityModule.Map(await Load(id, ct));

    public async Task<string> CreateAsync(CreateUserInput x, CancellationToken ct)
    {
        Guard.Against(!Enum.TryParse<UserRole>(x.Role, true, out var role), "user.role", "Vai trò không hợp lệ.");
        var user = AppUser.Create(Guid.NewGuid().ToString("N"), x.EntraObjectId, x.FullName, x.Email, x.Phone, role);
        user.Assign(x.WarehouseCode, x.AreaCode, x.RetailerCode);
        users.Add(user); await uow.SaveChangesAsync(ct); return user.Id;
    }

    public async Task UpdateAsync(string id, UpdateUserInput x, CancellationToken ct)
    {
        var u = await Load(id, ct);
        u.Update(x.FullName, x.Email, x.Phone, x.Active); u.Assign(x.WarehouseCode, x.AreaCode ?? u.AreaCode, x.RetailerCode);
        await uow.SaveChangesAsync(ct);
    }

    public async Task RegisterDeviceAsync(string userId, RegisterDeviceInput x, CancellationToken ct)
    {
        (await Load(userId, ct)).RegisterDevice(x.DeviceId, x.Platform, x.PushToken, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<AppUser> Load(string id, CancellationToken ct) => await users.GetAsync(id, ct) ?? throw new BusinessRuleException("user.not_found", $"Không có người dùng {id}.");
}

public sealed class IdentityModule(IUserRepository users) : IIdentityModule
{
    public async Task<UserDto?> GetUserAsync(string userId, CancellationToken ct = default) => await users.GetAsync(userId, ct) is { } u ? Map(u) : null;
    public async Task<IReadOnlyList<UserDto>> UsersInRoleAsync(string role, string? warehouseCode = null, CancellationToken ct = default) =>
        Enum.TryParse<UserRole>(role, true, out var r) ? (await users.InRoleAsync(r, warehouseCode, ct)).Select(Map).ToList() : Array.Empty<UserDto>();
    public async Task<IReadOnlyList<UserDto>> UsersOfRetailerAsync(string retailerCode, CancellationToken ct = default) => (await users.OfRetailerAsync(retailerCode, ct)).Select(Map).ToList();
    public async Task<IReadOnlyList<DeviceDto>> DevicesOfAsync(string userId, CancellationToken ct = default) =>
        (await users.GetAsync(userId, ct))?.Devices.Select(d => new DeviceDto(d.DeviceId, d.Platform, d.PushToken)).ToList() ?? new List<DeviceDto>();

    internal static UserDto Map(AppUser u) => new(u.Id, u.FullName, u.Email, u.Phone, u.Role.ToString(), u.WarehouseCode, u.SalesGroupCode, u.RetailerCode, u.Active);
}

/// <summary>Partners owns Sales ↔ group; Identity keeps a copy on the user so ICurrentUser.SalesGroupCode is cheap.</summary>
public sealed class SalesGroupMembersChangedHandler(IUserRepository users, IIdentityUnitOfWork uow) : IIntegrationEventHandler<SalesGroupMembersChanged>
{
    public async Task HandleAsync(SalesGroupMembersChanged e, CancellationToken ct = default)
    {
        foreach (var u in await users.ByIdsAsync(e.MemberUserIds.ToList(), ct)) u.SetSalesGroup(e.GroupCode, e.AreaCode);
        foreach (var u in await users.ByIdsAsync(e.RemovedUserIds.ToList(), ct)) if (u.SalesGroupCode == e.GroupCode) u.SetSalesGroup(null, null);
        await uow.SaveChangesAsync(ct);
    }
}

public static class IdentityApplication
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection s) => s
        .AddScoped<IIdentityModule, IdentityModule>()
        .AddScoped<UserService>()
        .AddScoped<IIntegrationEventHandler<SalesGroupMembersChanged>, SalesGroupMembersChangedHandler>();
}
