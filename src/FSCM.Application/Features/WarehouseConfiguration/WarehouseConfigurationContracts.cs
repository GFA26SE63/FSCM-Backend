using System.Text.Json;

namespace FSCM.Application.Features.WarehouseConfiguration;

public sealed record WarehouseResult(
    Guid Id,
    string Code,
    string Name,
    string Type,
    string? Address,
    decimal Latitude,
    decimal Longitude,
    string? ProviderPlaceId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveWarehouseData(
    string Code,
    string Name,
    string Type,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string? ProviderPlaceId);

public sealed record TransitTimeRuleData(
    int MinDistanceMeters,
    int MaxDistanceMeters,
    int HandlingMinutes,
    int TransitMinutes,
    int MaxServingDistanceMeters);

public sealed record TransitTimeRuleResult(
    Guid Id,
    Guid WarehouseId,
    int MinDistanceMeters,
    int MaxDistanceMeters,
    int HandlingMinutes,
    int TransitMinutes,
    int MaxServingDistanceMeters,
    int Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MlorResult(
    Guid Id,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    decimal MinimumLifeRatio,
    int? MinimumLifeHours,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive);

public sealed record ConfigurationResult(
    Guid Id,
    string Key,
    JsonElement Value,
    string Description,
    string ValueType,
    int Version,
    bool IsActive,
    Guid? UpdatedByUserId,
    DateTimeOffset EffectiveFrom);

public sealed record BusinessConfigurationDefinition(string ValueType, string Description);

public static class BusinessConfigurationKeys
{
    public const string AllocationWeights = "allocation.weights";
    public const string ServiceRadiusMeters = "allocation.service_radius_meters";
    public const string RegionalMinimumStockCartons = "replenishment.regional_minimum_stock_cartons";
    public const string DefaultMlorRatio = "mlor.default_ratio";
    public const string OrderBufferStockCartons = "order.buffer_stock_cartons";
    public const string OrderStepCartons = "order.order_step_cartons";

    public static readonly IReadOnlyDictionary<string, BusinessConfigurationDefinition> Supported =
        new Dictionary<string, BusinessConfigurationDefinition>(StringComparer.Ordinal)
        {
            [AllocationWeights] = new("object", "Trọng số chấm điểm phân bổ theo hạn sử dụng, khoảng cách và độ phủ"),
            [ServiceRadiusMeters] = new("integer", "Bán kính phục vụ tối đa mặc định, tính bằng mét"),
            [DefaultMlorRatio] = new("decimal", "Tỷ lệ hạn sử dụng còn lại tối thiểu mặc định"),
            [OrderBufferStockCartons] = new("integer", "Số thùng tồn kho đệm không dùng để phân bổ đơn hàng"),
            [OrderStepCartons] = new("integer", "Bước số lượng thùng hợp lệ khi tạo đơn hàng")
        };
}
