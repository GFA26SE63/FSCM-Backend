using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Roles;

public sealed class RoleService(
    IRoleStore store,
    TimeProvider timeProvider)
{
    private static readonly IReadOnlyList<ScreenFeatureDto> ScreenCatalog = new[]
    {
        // 1. Tổng quan
        new ScreenFeatureDto(SystemScreenKeys.Dashboard, "Tổng quan & báo cáo", "Xem dashboard hiệu suất kinh doanh, số liệu vận hành và biểu đồ doanh thu", "overview", "Tổng quan"),
        new ScreenFeatureDto(SystemScreenKeys.Reports, "Báo cáo đổi quà & CTKM", "Báo cáo chi tiết đổi quà, tích điểm và chương trình khuyến mãi", "overview", "Tổng quan"),
        new ScreenFeatureDto(SystemScreenKeys.WarehouseReport, "Báo cáo kho vận", "Báo cáo nhập xuất tồn, lưu lượng luân chuyển và hiệu suất kho", "overview", "Tổng quan"),
        new ScreenFeatureDto(SystemScreenKeys.Kpi, "KPI & Xếp hạng", "Theo dõi KPI doanh số, chỉ tiêu bán hàng và xếp hạng đại lý / nhân viên", "overview", "Tổng quan"),

        // 2. Bán hàng & Tài chính
        new ScreenFeatureDto(SystemScreenKeys.Orders, "Đơn hàng", "Quản lý danh sách đơn hàng, phê duyệt, rút đơn và theo dõi trạng thái", "sales", "Bán hàng & Tài chính"),
        new ScreenFeatureDto(SystemScreenKeys.Allocation, "Phân bổ thời gian thực", "Theo dõi kết quả phân bổ tồn kho tự động cho các đơn hàng", "sales", "Bán hàng & Tài chính"),
        new ScreenFeatureDto(SystemScreenKeys.Debts, "Công nợ", "Quản lý công nợ khách hàng, hạn mức tín dụng và thanh toán PayOS / thu tiền", "sales", "Bán hàng & Tài chính"),
        new ScreenFeatureDto(SystemScreenKeys.Refunds, "Hoàn tiền", "Xử lý yêu cầu hoàn tiền, đính kèm chứng từ và phê duyệt hoàn tiền", "sales", "Bán hàng & Tài chính"),
        new ScreenFeatureDto(SystemScreenKeys.Complaints, "Khiếu nại & Yêu cầu", "Tiếp nhận khiếu nại hàng hóa, thu thập bằng chứng và phê duyệt xử lý", "sales", "Bán hàng & Tài chính"),

        // 3. Kho vận
        new ScreenFeatureDto(SystemScreenKeys.WarehouseInbound, "Nhập kho (PNK)", "Tạo và kiểm duyệt phiếu nhập kho từ nhà máy và chuyển kho", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.WarehouseOutbound, "Xuất kho (PXK)", "Lập và xử lý phiếu xuất kho giao hàng cho đại lý / luân chuyển", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.WarehouseTransfers, "Luân chuyển kho", "Quản lý điều chuyển hàng hóa giữa các kho nội bộ", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.Batches, "Quản lý lô hàng", "Theo dõi chi tiết số lô, hạn sử dụng, in mã QR và truy xuất nguồn gốc", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.WarehouseTrace, "Tra cứu kiện / thùng", "Quét mã QR tra cứu thông tin thùng hàng và lịch sử luân chuyển", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.Readiness, "Điều phối giao hàng", "Chuẩn bị đơn hàng, phân công xe, tài xế và kế hoạch vận chuyển", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.BatchPromo, "Giảm giá theo HSD", "Thiết lập chính sách giảm giá linh hoạt cho các lô hàng cận hạn", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.Disposal, "Hủy hàng hết hạn", "Lập biên bản tiêu hủy, kiểm duyệt và xóa sổ hàng hư hại / hết date", "warehouse", "Kho vận"),
        new ScreenFeatureDto(SystemScreenKeys.Fleet, "Xe & Tài xế", "Quản lý danh sách phương tiện vận tải và đội ngũ tài xế", "warehouse", "Kho vận"),

        // 4. Khách hàng & Khuyến mãi
        new ScreenFeatureDto(SystemScreenKeys.Retailers, "Nhà bán lẻ", "Hồ sơ điểm bán, hạn mức nợ, lịch sử mua hàng và duyệt hồ sơ đại lý", "customer", "Khách hàng & Khuyến mãi"),
        new ScreenFeatureDto(SystemScreenKeys.Promos, "Khuyến mãi", "Thiết lập chương trình chiết khấu thương mại và khuyến mãi mua kèm", "customer", "Khách hàng & Khuyến mãi"),
        new ScreenFeatureDto(SystemScreenKeys.Loyalty, "Tích điểm", "Cấu hình quy tắc tích lũy điểm thưởng thành viên và tỷ lệ quy đổi", "customer", "Khách hàng & Khuyến mãi"),
        new ScreenFeatureDto(SystemScreenKeys.Redeem, "Đổi quà", "Xử lý duyệt yêu cầu đổi quà tích điểm của nhà bán lẻ", "customer", "Khách hàng & Khuyến mãi"),
        new ScreenFeatureDto(SystemScreenKeys.Gifts, "Danh mục quà tặng", "Quản lý danh mục quà đổi thưởng, số lượng tồn và định giá điểm", "customer", "Khách hàng & Khuyến mãi"),

        // 5. Danh mục
        new ScreenFeatureDto(SystemScreenKeys.Regions, "Khu vực & Nhóm bán hàng", "Cấu hình phân vùng địa lý kinh doanh và phân bổ nhóm phụ trách", "catalog", "Danh mục"),
        new ScreenFeatureDto(SystemScreenKeys.Cats, "Danh mục ngành hàng", "Quản lý nhóm phân loại sản phẩm FMCG", "catalog", "Danh mục"),
        new ScreenFeatureDto(SystemScreenKeys.CatMlor, "HSD tối thiểu (MLOR)", "Quy định tỷ lệ hạn sử dụng tối thiểu khi xuất giao hàng cho từng ngành", "catalog", "Danh mục"),
        new ScreenFeatureDto(SystemScreenKeys.Skus, "Sản phẩm (SKU)", "Danh mục mã sản phẩm, đơn giá niêm yết, quy cách đóng gói", "catalog", "Danh mục"),
        new ScreenFeatureDto(SystemScreenKeys.Whs, "Kho hàng", "Quản lý mạng lưới các kho trung tâm và kho khu vực", "catalog", "Danh mục"),

        // 6. Hệ thống
        new ScreenFeatureDto(SystemScreenKeys.Users, "Người dùng & Tài khoản", "Tạo tài khoản người dùng, phân quyền vai trò và khóa tài khoản", "system", "Hệ thống"),
        new ScreenFeatureDto(SystemScreenKeys.Roles, "Vai trò & Phân quyền", "Định nghĩa các vai trò trong hệ thống và phân quyền truy cập màn hình chức năng", "system", "Hệ thống"),
        new ScreenFeatureDto(SystemScreenKeys.Config, "Cấu hình hệ thống", "Tham số vận hành cốt lõi, ngưỡng cảnh báo và tích hợp", "system", "Hệ thống")
    };

    public IReadOnlyList<ScreenFeatureDto> GetScreenFeatures() => ScreenCatalog;

    public async Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(string? search, CancellationToken ct)
    {
        await store.EnsureDefaultRolesSeededAsync(timeProvider, ct);

        var list = await store.ListRolesAsync(search, ct);

        return list
            .OrderBy(item => item.Role.Code == SystemRoles.Admin ? 0 : item.Role.IsSystem ? 1 : 2)
            .ThenBy(item => item.Role.Name)
            .Select(item => new RoleSummaryDto(
                item.Role.Id,
                item.Role.Code,
                item.Role.Name,
                item.Role.Description,
                item.Role.IsSystem,
                item.Role.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList(),
                item.UserCount,
                item.Role.CreatedAt,
                item.Role.UpdatedAt))
            .ToList();
    }

    public async Task<RoleDetailsDto> GetRoleByIdAsync(Guid roleId, CancellationToken ct)
    {
        await store.EnsureDefaultRolesSeededAsync(timeProvider, ct);

        var role = await store.FindByIdAsync(roleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vai trò với ID đã cung cấp.");

        var userCount = await store.CountUsersAssignedAsync(role.Code, ct);

        return new RoleDetailsDto(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystem,
            role.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList(),
            userCount,
            role.CreatedAt,
            role.UpdatedAt);
    }

    public async Task<RoleDetailsDto> CreateRoleAsync(CreateRoleRequest request, Guid actorUserId, CancellationToken ct)
    {
        await store.EnsureDefaultRolesSeededAsync(timeProvider, ct);

        var code = request.Code?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Mã vai trò không được để trống.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên vai trò không được để trống.");

        // Rule: Admin role cannot be created or duplicated
        if (string.Equals(code, SystemRoles.Admin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không thể tạo hoặc trùng với vai trò Quản trị viên (Admin).");

        if (code.Length > 50)
            throw new ArgumentException("Mã vai trò không được vượt quá 50 ký tự.");
        if (name.Length > 100)
            throw new ArgumentException("Tên vai trò không được vượt quá 100 ký tự.");
        if (description?.Length > 500)
            throw new ArgumentException("Mô tả không được vượt quá 500 ký tự.");

        if (await store.ExistsByCodeAsync(code, ct))
            throw new InvalidOperationException($"Mã vai trò '{code}' đã tồn tại trong hệ thống.");

        var now = timeProvider.GetUtcNow();
        var role = new RoleDefinition
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = description,
            IsSystem = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        var validKeys = SystemScreenKeys.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.Permissions is not null && request.Permissions.Count > 0)
        {
            foreach (var key in request.Permissions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (validKeys.Contains(key))
                {
                    role.Permissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(),
                        RoleId = role.Id,
                        PermissionKey = key,
                        CreatedAt = now
                    });
                }
            }
        }

        store.AddRole(role);
        await store.SaveChangesAsync(ct);

        return new RoleDetailsDto(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystem,
            role.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList(),
            0,
            role.CreatedAt,
            role.UpdatedAt);
    }

    public async Task<RoleDetailsDto> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request, Guid actorUserId, CancellationToken ct)
    {
        var role = await store.FindByIdAsync(roleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vai trò cần chỉnh sửa.");

        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên vai trò không được để trống.");
        if (name.Length > 100)
            throw new ArgumentException("Tên vai trò không được vượt quá 100 ký tự.");
        if (description?.Length > 500)
            throw new ArgumentException("Mô tả không được vượt quá 500 ký tự.");

        var now = timeProvider.GetUtcNow();
        role.Name = name;
        role.Description = description;
        role.UpdatedAt = now;

        // Admin role always has all permissions and cannot have its permissions stripped
        if (role.Code != SystemRoles.Admin)
        {
            var validKeys = SystemScreenKeys.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filteredKeys = (request.Permissions ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(validKeys.Contains)
                .ToList();

            store.ReplacePermissions(role, filteredKeys, now);
        }

        await store.SaveChangesAsync(ct);

        var userCount = await store.CountUsersAssignedAsync(role.Code, ct);

        return new RoleDetailsDto(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystem,
            role.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList(),
            userCount,
            role.CreatedAt,
            role.UpdatedAt);
    }

    public async Task DeleteRoleAsync(Guid roleId, Guid actorUserId, CancellationToken ct)
    {
        var role = await store.FindByIdAsync(roleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vai trò cần xóa.");

        if (role.IsSystem || string.Equals(role.Code, SystemRoles.Admin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không thể xóa vai trò hệ thống.");

        if (await store.HasUsersAssignedAsync(role.Code, ct))
            throw new InvalidOperationException($"Không thể xóa vai trò '{role.Name}' vì đang có người dùng được gán vai trò này. Hãy chuyển vai trò của người dùng trước khi xóa.");

        store.RemoveRole(role);
        await store.SaveChangesAsync(ct);
    }
}
