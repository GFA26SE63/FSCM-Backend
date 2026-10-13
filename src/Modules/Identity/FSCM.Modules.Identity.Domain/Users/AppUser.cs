using FSCM.SharedKernel.Domain;

namespace FSCM.Modules.Identity.Domain.Users;

public enum UserRole { Admin, Operator, SalesManager, WarehouseKeeper, Sales, Retailer }

/// <summary>Push registration of one mobile device (Azure Notification Hubs handle / FCM token).</summary>
public sealed class UserDevice
{
    private UserDevice() { }
    public UserDevice(string deviceId, string platform, string pushToken, DateTime nowUtc) { DeviceId = deviceId; Platform = platform; PushToken = pushToken; RegisteredAtUtc = nowUtc; }
    public string DeviceId { get; private set; } = "";
    public string Platform { get; private set; } = "";
    public string PushToken { get; private set; } = "";
    public DateTime RegisteredAtUtc { get; private set; }
    internal void Refresh(string platform, string token, DateTime nowUtc) { Platform = platform; PushToken = token; RegisteredAtUtc = nowUtc; }
}

/// <summary>
/// Application user linked to a Microsoft Entra ID object id. Entra authenticates; this record carries the
/// business role and the assignment (warehouse for a keeper, sales group for Sales, retailer for a retailer account).
/// Admin accounts are seeded and can never be created from the UI.
/// </summary>
public sealed class AppUser : AggregateRoot<string>
{
    private readonly List<UserDevice> _devices = new();
    private AppUser() { }
    private AppUser(string id, string? entraObjectId, string fullName, string email, string phone, UserRole role)
    {
        Guard.Against(string.IsNullOrWhiteSpace(fullName), "user.name", "Họ tên bắt buộc.");
        Id = id; EntraObjectId = entraObjectId; FullName = fullName.Trim(); Email = email; Phone = phone; Role = role; Active = true;
    }
    public string? EntraObjectId { get; private set; }
    public string FullName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public UserRole Role { get; private set; }
    public bool Active { get; private set; }
    public string? WarehouseCode { get; private set; }
    public string? AreaCode { get; private set; }
    public string? SalesGroupCode { get; private set; }
    public string? RetailerCode { get; private set; }
    public IReadOnlyCollection<UserDevice> Devices => _devices.AsReadOnly();

    /// <summary>Creation from the Portal "Người dùng" screen. Admin is not allowed.</summary>
    public static AppUser Create(string id, string? entraObjectId, string fullName, string email, string phone, UserRole role)
    {
        Guard.Against(role == UserRole.Admin, "user.admin", "Không tạo tài khoản Admin từ giao diện – Admin được seed sẵn.");
        return new AppUser(id, entraObjectId, fullName, email, phone, role);
    }
    /// <summary>Only for the seeder / deployment script.</summary>
    public static AppUser SeedAdmin(string id, string? entraObjectId, string fullName, string email) => new(id, entraObjectId, fullName, email, "", UserRole.Admin);

    public void Update(string fullName, string email, string phone, bool active)
    {
        Guard.Against(string.IsNullOrWhiteSpace(fullName), "user.name", "Họ tên bắt buộc.");
        Guard.Against(!active && Role == UserRole.Admin, "user.admin", "Không vô hiệu hoá tài khoản Admin.");
        FullName = fullName.Trim(); Email = email; Phone = phone; Active = active;
    }
    public void LinkEntra(string objectId) => EntraObjectId = objectId;

    public void Assign(string? warehouseCode, string? areaCode, string? retailerCode)
    {
        Guard.Against(warehouseCode is not null && Role != UserRole.WarehouseKeeper, "user.assign", "Chỉ thủ kho được gán kho.");
        Guard.Against(retailerCode is not null && Role != UserRole.Retailer, "user.assign", "Chỉ tài khoản retailer được gắn mã retailer.");
        Guard.Against(Role == UserRole.WarehouseKeeper && string.IsNullOrWhiteSpace(warehouseCode), "user.assign", "Thủ kho phải được gán một kho.");
        WarehouseCode = warehouseCode; AreaCode = areaCode; RetailerCode = retailerCode;
    }
    /// <summary>Kept in sync from Partners (SalesGroupMembersChanged); Sales ↔ group is owned by Partners.</summary>
    public void SetSalesGroup(string? groupCode, string? areaCode) { SalesGroupCode = groupCode; AreaCode = areaCode ?? AreaCode; }

    public void RegisterDevice(string deviceId, string platform, string pushToken, DateTime nowUtc)
    {
        Guard.Against(string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(pushToken), "device.invalid", "Thiếu mã thiết bị hoặc push token.");
        var d = _devices.FirstOrDefault(x => x.DeviceId == deviceId);
        if (d is null) _devices.Add(new UserDevice(deviceId, platform, pushToken, nowUtc)); else d.Refresh(platform, pushToken, nowUtc);
    }
}
