using System.Text.Json;
using System.Text.RegularExpressions;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.WarehouseConfiguration;

public sealed partial class WarehouseConfigurationService(
    IWarehouseConfigurationStore store,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<WarehouseResult>> ListWarehousesAsync(bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListWarehousesAsync(includeInactive, cancellationToken)).Select(ToWarehouseResult).ToArray();

    public async Task<WarehouseResult> GetWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        ToWarehouseResult(await RequireWarehouseViewAsync(warehouseId, cancellationToken));

    public async Task<WarehouseResult> CreateWarehouseAsync(SaveWarehouseData data, CancellationToken cancellationToken)
    {
        var normalized = ValidateWarehouse(data, includeCode: true);
        if (await store.FindWarehouseByCodeAsync(normalized.Code, cancellationToken) is not null)
        {
            throw WarehouseConfigurationOperationException.Conflict($"Warehouse code '{normalized.Code}' already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            Code = normalized.Code,
            Name = normalized.Name,
            Type = normalized.Type,
            Address = normalized.Address,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var location = CreateLocation(warehouse.Id, normalized, now);
        store.AddWarehouse(warehouse);
        store.AddLocation(location);
        await store.SaveChangesAsync(cancellationToken);
        return ToWarehouseResult(new WarehouseView(warehouse, location));
    }

    public async Task<WarehouseResult> UpdateWarehouseAsync(Guid warehouseId, SaveWarehouseData data, CancellationToken cancellationToken)
    {
        var normalized = ValidateWarehouse(data, includeCode: false);
        var warehouse = await store.FindWarehouseAsync(warehouseId, cancellationToken)
            ?? throw WarehouseConfigurationOperationException.NotFound("Warehouse was not found.");
        var location = await store.FindWarehouseLocationAsync(warehouseId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        warehouse.Name = normalized.Name;
        warehouse.Type = normalized.Type;
        warehouse.Address = normalized.Address;
        warehouse.UpdatedAt = now;
        if (location is null)
        {
            location = CreateLocation(warehouseId, normalized, now);
            store.AddLocation(location);
        }
        else
        {
            location.Latitude = normalized.Latitude;
            location.Longitude = normalized.Longitude;
            location.FormattedAddress = normalized.Address;
            location.ProviderPlaceId = normalized.ProviderPlaceId;
            location.UpdatedAt = now;
        }

        await store.SaveChangesAsync(cancellationToken);
        return ToWarehouseResult(new WarehouseView(warehouse, location));
    }

    public async Task SetWarehouseStatusAsync(Guid warehouseId, bool isActive, CancellationToken cancellationToken)
    {
        var warehouse = await store.FindWarehouseAsync(warehouseId, cancellationToken)
            ?? throw WarehouseConfigurationOperationException.NotFound("Warehouse was not found.");
        warehouse.IsActive = isActive;
        warehouse.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TransitTimeRuleResult>> ListTransitRulesAsync(
        Guid warehouseId, bool includeInactive, CancellationToken cancellationToken)
    {
        await RequireWarehouseAsync(warehouseId, cancellationToken);
        return (await store.ListTransitRulesAsync(warehouseId, includeInactive, cancellationToken))
            .Select(ToTransitResult).ToArray();
    }

    public async Task<Dictionary<Guid, TransitTimeRuleResult[]>> ListTransitRulesByWarehouseIdsAsync(
        IReadOnlyCollection<Guid> warehouseIds,
        CancellationToken cancellationToken)
    {
        var rules = await store.ListTransitRulesAsync(warehouseIds.Distinct().ToArray(), false, cancellationToken);
        return rules.GroupBy(rule => rule.WarehouseId)
            .ToDictionary(group => group.Key, group => group.Select(ToTransitResult).ToArray());
    }

    public async Task<IReadOnlyCollection<TransitTimeRuleResult>> ReplaceTransitRulesAsync(
        Guid warehouseId, IReadOnlyCollection<TransitTimeRuleData> definitions, CancellationToken cancellationToken)
    {
        await RequireWarehouseAsync(warehouseId, cancellationToken);
        ValidateTransitRules(definitions);
        var now = timeProvider.GetUtcNow();
        var existing = await store.ListTransitRulesAsync(warehouseId, false, cancellationToken);
        foreach (var rule in existing)
        {
            rule.IsActive = false;
            rule.UpdatedAt = now;
        }

        var rules = definitions.OrderBy(item => item.MinDistanceMeters).Select(item => new TransitTimeRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            MinDistanceMeters = item.MinDistanceMeters,
            MaxDistanceMeters = item.MaxDistanceMeters,
            HandlingMinutes = item.HandlingMinutes,
            TransitMinutes = item.TransitMinutes,
            MaxServingDistanceMeters = item.MaxServingDistanceMeters,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        }).ToArray();
        store.AddTransitRules(rules);
        await store.SaveChangesAsync(cancellationToken);
        return rules.Select(ToTransitResult).ToArray();
    }

    public async Task<IReadOnlyCollection<MlorResult>> ListMlorAsync(
        Guid? retailerId, Guid? productId, bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListMlorAsync(retailerId, productId, includeInactive, cancellationToken)).Select(ToMlorResult).ToArray();

    public async Task<MlorResult> SetMlorAsync(
        Guid retailerId, Guid productId, decimal minimumLifeRatio, int? minimumLifeHours,
        CancellationToken cancellationToken)
    {
        if (minimumLifeRatio <= 0 || minimumLifeRatio > 1)
        {
            throw WarehouseConfigurationOperationException.Validation("Minimum life ratio must be greater than 0 and at most 1.");
        }

        var retailer = await store.FindRetailerAsync(retailerId, cancellationToken)
            ?? throw WarehouseConfigurationOperationException.NotFound("Retailer was not found.");
        var product = await store.FindProductAsync(productId, cancellationToken)
            ?? throw WarehouseConfigurationOperationException.NotFound("Product was not found.");
        if (minimumLifeHours is < 0 || minimumLifeHours > product.ShelfLifeHours)
        {
            throw WarehouseConfigurationOperationException.Validation("Minimum life hours must be between 0 and the product shelf life.");
        }

        var now = timeProvider.GetUtcNow();
        var current = await store.FindActiveMlorAsync(retailerId, productId, cancellationToken);
        if (current is not null)
        {
            current.IsActive = false;
            current.EffectiveTo = now;
        }

        var mlor = new RetailerProductMlor
        {
            Id = Guid.NewGuid(),
            RetailerId = retailerId,
            ProductId = productId,
            MinimumLifeRatio = minimumLifeRatio,
            MinimumLifeHours = minimumLifeHours,
            EffectiveFrom = now,
            IsActive = true
        };
        store.AddMlor(mlor);
        await store.SaveChangesAsync(cancellationToken);
        return ToMlorResult(new MlorView(mlor, retailer.Code, retailer.Name, product.Sku, product.Name));
    }

    public async Task<IReadOnlyCollection<ConfigurationResult>> ListCurrentConfigurationsAsync(CancellationToken cancellationToken) =>
        (await store.ListCurrentConfigurationsAsync(cancellationToken)).Select(ToConfigurationResult).ToArray();

    public async Task<IReadOnlyCollection<ConfigurationResult>> ListConfigurationHistoryAsync(
        string key, CancellationToken cancellationToken)
    {
        key = NormalizeConfigurationKey(key);
        return (await store.ListConfigurationHistoryAsync(key, cancellationToken)).Select(ToConfigurationResult).ToArray();
    }

    public async Task<ConfigurationResult> UpdateConfigurationAsync(
        string key, string rawJsonValue, Guid updatedByUserId, CancellationToken cancellationToken)
    {
        key = NormalizeConfigurationKey(key);
        var definition = BusinessConfigurationKeys.Supported[key];
        var canonicalJson = ValidateConfigurationValue(key, rawJsonValue);
        var history = await store.ListConfigurationHistoryAsync(key, cancellationToken);
        var current = history.SingleOrDefault(item => item.IsActive);
        if (current is not null && JsonEquivalent(current.ConfigValue, canonicalJson))
        {
            return ToConfigurationResult(current);
        }

        if (current is not null)
        {
            current.IsActive = false;
            current.UpdatedAt = timeProvider.GetUtcNow();
        }

        var now = timeProvider.GetUtcNow();
        var configuration = new SystemConfiguration
        {
            Id = Guid.NewGuid(),
            ConfigKey = key,
            ConfigValue = canonicalJson,
            Description = definition.Description,
            ValueType = definition.ValueType,
            Version = history.Count == 0 ? 1 : history.Max(item => item.Version) + 1,
            IsActive = true,
            UpdatedByUserId = updatedByUserId,
            EffectiveFrom = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddConfiguration(configuration);
        await store.SaveChangesAsync(cancellationToken);
        return ToConfigurationResult(configuration);
    }

    private async Task<WarehouseView> RequireWarehouseViewAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindWarehouseViewAsync(id, cancellationToken)
        ?? throw WarehouseConfigurationOperationException.NotFound("Warehouse was not found.");

    private async Task<Warehouse> RequireWarehouseAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindWarehouseAsync(id, cancellationToken)
        ?? throw WarehouseConfigurationOperationException.NotFound("Warehouse was not found.");

    private static SaveWarehouseData ValidateWarehouse(SaveWarehouseData data, bool includeCode)
    {
        var code = data.Code.Trim().ToUpperInvariant();
        var name = data.Name.Trim();
        var address = data.Address.Trim();
        var type = data.Type.Trim();
        if (includeCode && !WarehouseCodeRegex().IsMatch(code))
            throw WarehouseConfigurationOperationException.Validation("Warehouse code must start with WH- and contain uppercase letters, digits, or hyphens.");
        if (name.Length is < 2 or > 150) throw WarehouseConfigurationOperationException.Validation("Warehouse name must contain 2 to 150 characters.");
        if (!WarehouseTypes.All.Contains(type)) throw WarehouseConfigurationOperationException.Validation("Warehouse type must be Central or Regional.");
        if (address.Length is < 5 or > 500) throw WarehouseConfigurationOperationException.Validation("Warehouse address must contain 5 to 500 characters.");
        if (data.Latitude is < -90 or > 90 || data.Longitude is < -180 or > 180)
            throw WarehouseConfigurationOperationException.Validation("Warehouse coordinates are outside the valid latitude or longitude range.");
        if (data.ProviderPlaceId?.Length > 200) throw WarehouseConfigurationOperationException.Validation("Provider place ID cannot exceed 200 characters.");
        return data with { Code = code, Name = name, Type = type, Address = address, ProviderPlaceId = data.ProviderPlaceId?.Trim() };
    }

    private static void ValidateTransitRules(IReadOnlyCollection<TransitTimeRuleData> definitions)
    {
        if (definitions.Count == 0) throw WarehouseConfigurationOperationException.Validation("At least one transit-time rule is required.");
        var ordered = definitions.OrderBy(item => item.MinDistanceMeters).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var rule = ordered[index];
            if (rule.MinDistanceMeters < 0 || rule.MaxDistanceMeters <= rule.MinDistanceMeters ||
                rule.HandlingMinutes < 0 || rule.TransitMinutes <= 0 || rule.MaxServingDistanceMeters < rule.MaxDistanceMeters)
                throw WarehouseConfigurationOperationException.Validation("Transit-time ranges, durations, and serving distance must be positive and internally consistent.");
            if (index > 0 && rule.MinDistanceMeters <= ordered[index - 1].MaxDistanceMeters)
                throw WarehouseConfigurationOperationException.Validation("Active transit-time distance ranges cannot overlap.");
        }
    }

    private static string NormalizeConfigurationKey(string key)
    {
        key = key.Trim().ToLowerInvariant();
        if (!BusinessConfigurationKeys.Supported.ContainsKey(key))
            throw WarehouseConfigurationOperationException.Validation($"Configuration key '{key}' is not supported.");
        return key;
    }

    private static string ValidateConfigurationValue(string key, string rawJson)
    {
        JsonElement value;
        try { using var document = JsonDocument.Parse(rawJson); value = document.RootElement.Clone(); }
        catch (JsonException) { throw WarehouseConfigurationOperationException.Validation("Configuration value must be valid JSON."); }

        switch (key)
        {
            case BusinessConfigurationKeys.AllocationWeights:
                if (value.ValueKind != JsonValueKind.Object ||
                    !TryGetDecimal(value, "expiry", out var expiry) ||
                    !TryGetDecimal(value, "distance", out var distance) ||
                    !TryGetDecimal(value, "coverage", out var coverage) ||
                    expiry < 0 || distance < 0 || coverage < 0 || Math.Abs(expiry + distance + coverage - 1m) > 0.0001m)
                    throw WarehouseConfigurationOperationException.Validation("Allocation weights expiry, distance, and coverage must be non-negative and sum to 1.");
                break;
            case BusinessConfigurationKeys.ServiceRadiusMeters:
                if (!value.TryGetInt32(out var radius) || radius <= 0)
                    throw WarehouseConfigurationOperationException.Validation("Service radius must be a positive integer in meters.");
                break;
            case BusinessConfigurationKeys.RegionalMinimumStockCartons:
                if (!value.TryGetInt32(out var threshold) || threshold < 0)
                    throw WarehouseConfigurationOperationException.Validation("Regional minimum stock must be a non-negative integer.");
                break;
            case BusinessConfigurationKeys.DefaultMlorRatio:
                if (!value.TryGetDecimal(out var ratio) || ratio <= 0 || ratio > 1)
                    throw WarehouseConfigurationOperationException.Validation("Default MLOR ratio must be greater than 0 and at most 1.");
                break;
        }

        return JsonSerializer.Serialize(value);
    }

    private static bool TryGetDecimal(JsonElement element, string name, out decimal value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.TryGetDecimal(out value);
    }

    private static bool JsonEquivalent(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static MapLocation CreateLocation(Guid warehouseId, SaveWarehouseData data, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        WarehouseId = warehouseId,
        Latitude = data.Latitude,
        Longitude = data.Longitude,
        FormattedAddress = data.Address,
        ProviderPlaceId = data.ProviderPlaceId,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static WarehouseResult ToWarehouseResult(WarehouseView view) => new(
        view.Warehouse.Id, view.Warehouse.Code, view.Warehouse.Name, view.Warehouse.Type,
        view.Warehouse.Address, view.Location?.Latitude ?? 0, view.Location?.Longitude ?? 0,
        view.Location?.ProviderPlaceId, view.Warehouse.IsActive, view.Warehouse.CreatedAt, view.Warehouse.UpdatedAt);

    private static TransitTimeRuleResult ToTransitResult(TransitTimeRule rule) => new(
        rule.Id, rule.WarehouseId, rule.MinDistanceMeters, rule.MaxDistanceMeters,
        rule.HandlingMinutes, rule.TransitMinutes, rule.MaxServingDistanceMeters,
        rule.IsActive, rule.CreatedAt, rule.UpdatedAt);

    private static MlorResult ToMlorResult(MlorView view) => new(
        view.Mlor.Id, view.Mlor.RetailerId, view.RetailerCode, view.RetailerName,
        view.Mlor.ProductId, view.ProductSku, view.ProductName, view.Mlor.MinimumLifeRatio,
        view.Mlor.MinimumLifeHours, view.Mlor.EffectiveFrom, view.Mlor.EffectiveTo, view.Mlor.IsActive);

    private static ConfigurationResult ToConfigurationResult(SystemConfiguration item)
    {
        using var document = JsonDocument.Parse(item.ConfigValue);
        return new(item.Id, item.ConfigKey, document.RootElement.Clone(), item.Description, item.ValueType,
            item.Version, item.IsActive, item.UpdatedByUserId, item.EffectiveFrom);
    }

    [GeneratedRegex("^WH-[A-Z0-9]+(?:-[A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex WarehouseCodeRegex();
}
