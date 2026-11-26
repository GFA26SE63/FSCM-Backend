using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using System.Text.Json;

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
        new ScreenFeatureDto(SystemScreenKeys.WarehouseFactoryRequests, "Yêu cầu nhận hàng từ nhà máy", "Tiếp nhận phiếu xuất xưởng của nhà máy và mở phiếu kiểm đếm khi hàng đến kho", "warehouse", "Kho vận"),
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
        new ScreenFeatureDto(SystemScreenKeys.Config, "Cấu hình hệ thống", "Tham số vận hành cốt lõi, ngưỡng cảnh báo và tích hợp", "system", "Hệ thống"),

        // 7. Bán hàng di động (Mobile App - Sales)
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesHome, "Trang chủ & Tin tức", "Màn hình tổng quan kết quả bán hàng và bảng tin thị trường", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesOrders, "Đơn đặt hàng", "Theo dõi trạng thái và tiến độ xử lý đơn đặt hàng đại lý", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesOrderCreate, "Lập đơn hàng mới", "Tạo đơn hàng trực tiếp tại điểm bán của khách hàng", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesScan, "Quét mã QR bán hàng", "Quét QR thùng hàng, tem sản phẩm và kiểm tra khuyến mãi", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesRetailers, "Cửa hàng phụ trách", "Xem danh sách và chi tiết hồ sơ các điểm bán thuộc tuyến bán hàng", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesDeclare, "Khai báo điểm bán mới", "Lập hồ sơ đăng ký đại lý bán lẻ mới và chụp ảnh điểm bán", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesDeclarations, "Yêu cầu đã khai báo", "Theo dõi tình trạng phê duyệt các hồ sơ điểm bán đã nộp", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesProducts, "Sản phẩm & Tồn kho", "Tra cứu danh mục sản phẩm, bảng giá và số lượng tồn kho", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesPromos, "Chương trình khuyến mãi", "Tra cứu chính sách chiết khấu, khuyến mãi combo và bậc thang", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesDebts, "Công nợ khách hàng", "Theo dõi nợ phải thu, hạn mức tín dụng và hạn thanh toán của điểm bán", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesKpi, "KPI & Xếp hạng", "Theo dõi tiến độ hoàn thành doanh số tháng và bảng xếp hạng đội ngũ", "mobile-sales", "Bán hàng di động (Mobile Sales)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileSalesSync, "Đồng bộ ngoại tuyến", "Hàng đợi lưu trữ và tự động đồng bộ đơn hàng khi mất kết nối mạng", "mobile-sales", "Bán hàng di động (Mobile Sales)"),

        // 8. Nhà bán lẻ di động (Mobile App - Retailer)
        new ScreenFeatureDto(SystemScreenKeys.MobileRtOrders, "Đơn mua hàng", "Xem danh sách đơn đặt hàng, trạng thái giao vận và hóa đơn", "mobile-retailer", "Nhà bán lẻ di động (Mobile Retailer)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileRtOrderCreate, "Đặt hàng trực tiếp", "Tự tạo đơn đặt hàng nhập hàng FMCG từ nhà phân phối", "mobile-retailer", "Nhà bán lẻ di động (Mobile Retailer)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileRtScan, "Quét QR nhận hàng", "Quét mã QR tem thùng khi nhận hàng để xác thực nguồn gốc và niêm phong", "mobile-retailer", "Nhà bán lẻ di động (Mobile Retailer)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileRtPayments, "Thanh toán & Công nợ", "Thanh toán hóa đơn qua cổng PayOS và đối soát công nợ đại lý", "mobile-retailer", "Nhà bán lẻ di động (Mobile Retailer)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileRtLoyalty, "Thẻ tích điểm thành viên", "Tích lũy điểm thưởng theo giá trị đơn hàng và xếp hạng thành viên", "mobile-retailer", "Nhà bán lẻ di động (Mobile Retailer)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileRtGifts, "Đổi quà tích điểm", "Chọn quà tặng hấp dẫn trong danh mục và gửi yêu cầu đổi điểm", "mobile-retailer", "Nhà bán lẻ di động (Mobile Retailer)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileRtComplaints, "Khiếu nại & Hỗ trợ", "Gửi phản ánh hàng móp méo / sai lệch và yêu cầu thu hồi hàng", "mobile-retailer", "Nhà bán lẻ di động (Mobile Retailer)"),

        // 9. Kho vận di động (Mobile App - Warehouse)
        new ScreenFeatureDto(SystemScreenKeys.MobileWkPicking, "Lấy hàng theo đơn (Picking)", "Xem danh sách đơn cần nhặt hàng và quét xác nhận từng dòng hàng", "mobile-warehouse", "Kho vận di động (Mobile Warehouse)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileWkOperations, "Vận hành quét kho", "Thực hiện kiểm đếm nhập kho, xuất kho và chuyển kho trên thiết bị di động", "mobile-warehouse", "Kho vận di động (Mobile Warehouse)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileWkLabel, "Tra cứu tem kiện hàng", "Quét mã QR tem kiện/lô để kiểm tra thông tin HSD và số lượng thùng", "mobile-warehouse", "Kho vận di động (Mobile Warehouse)"),
        new ScreenFeatureDto(SystemScreenKeys.MobileWkShipped, "Lịch sử phiếu đã xuất", "Tra cứu danh sách các phiếu xuất hàng đã hoàn tất giao dịch", "mobile-warehouse", "Kho vận di động (Mobile Warehouse)")
    };

    public IReadOnlyList<ScreenFeatureDto> GetScreenFeatures() => ScreenCatalog.Concat(
        ApiPermissionCatalog.All.Select(p => new ScreenFeatureDto(p.Key, p.Action,
            $"API {p.Controller}.{p.Action}", "api:" + p.Controller, "API · " + p.Controller))).ToArray();

    public async Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(string? search, CancellationToken ct)
    {
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
                item.Role.UpdatedAt, item.Role.BaseRole))
            .ToList();
    }

    public async Task<RoleDetailsDto> GetRoleByIdAsync(Guid roleId, CancellationToken ct)
    {
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
            role.UpdatedAt, role.BaseRole);
    }

    public async Task<RoleDetailsDto> CreateRoleAsync(CreateRoleRequest request, Guid actorUserId, CancellationToken ct)
    {
        var code = request.Code?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (!SystemRoles.IsValid(request.BaseRole) || request.BaseRole == SystemRoles.Admin)
            throw new ArgumentException("Phạm vi nghiệp vụ phải là vai trò hệ thống không phải Admin.");

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
            BaseRole = request.BaseRole,
            Description = description,
            IsSystem = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        var validKeys = SystemPermissionKeys.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                        PermissionKey = SystemPermissionKeys.All.First(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)),
                        CreatedAt = now
                    });
                }
            }
        }

        store.AddRole(role);
        Audit(role, actorUserId, "Create", null, now);
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
            role.UpdatedAt, role.BaseRole);
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

        var oldValues = Snapshot(role);
        var now = timeProvider.GetUtcNow();
        role.Name = name;
        role.Description = description;
        role.UpdatedAt = now;

        // Admin role always has all permissions and cannot have its permissions stripped
        if (role.Code != SystemRoles.Admin)
        {
            var validKeys = SystemPermissionKeys.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filteredKeys = (request.Permissions ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(validKeys.Contains)
                .Select(key => SystemPermissionKeys.All.First(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            store.ReplacePermissions(role, filteredKeys, now);
        }

        Audit(role, actorUserId, "Update", oldValues, now);
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
            role.UpdatedAt, role.BaseRole);
    }

    public async Task DeleteRoleAsync(Guid roleId, Guid actorUserId, CancellationToken ct)
    {
        var role = await store.FindByIdAsync(roleId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy vai trò cần xóa.");

        if (role.IsSystem || string.Equals(role.Code, SystemRoles.Admin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không thể xóa vai trò hệ thống.");

        if (await store.HasUsersAssignedAsync(role.Code, ct))
            throw new InvalidOperationException($"Không thể xóa vai trò '{role.Name}' vì đang có người dùng được gán vai trò này. Hãy chuyển vai trò của người dùng trước khi xóa.");

        Audit(role, actorUserId, "Delete", Snapshot(role), timeProvider.GetUtcNow());
        store.RemoveRole(role);
        await store.SaveChangesAsync(ct);
    }

    private static string Snapshot(RoleDefinition role) => JsonSerializer.Serialize(new
    {
        role.Code, role.Name, role.Description, role.BaseRole,
        Permissions = role.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToArray()
    });

    private void Audit(RoleDefinition role, Guid actorUserId, string action, string? oldValues, DateTimeOffset now) =>
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(), UserAccountId = actorUserId, EntityName = "RoleDefinition",
            EntityId = role.Id, Action = action, OldValues = oldValues,
            NewValues = action == "Delete" ? null : Snapshot(role), CreatedAt = now
        });
}
