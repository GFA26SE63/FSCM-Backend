using System.Text.Json;
using System.Text.RegularExpressions;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.WarehouseConfiguration;

public sealed partial class WarehouseConfigurationService(
    IWarehouseConfigurationStore store,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<WarehouseResult>> ListWarehousesAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var views = await store.ListWarehousesAsync(includeInactive, cancellationToken);
        var keepers = await ListWarehouseKeeperLookupAsync(
            views.Select(item => item.Warehouse.Id), cancellationToken);
        return views.Select(item => ToWarehouseResult(
            item, keepers.GetValueOrDefault(item.Warehouse.Id, []))).ToArray();
    }

    public async Task<WarehouseResult> GetWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var view = await RequireWarehouseViewAsync(warehouseId, cancellationToken);
        var keepers = await store.ListActiveWarehouseKeepersAsync([warehouseId], cancellationToken);
        return ToWarehouseResult(view, keepers);
    }

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
            RegionId = normalized.RegionId,
            Address = normalized.Address,
            MinimumStockThresholdCartons = normalized.MinimumStockThresholdCartons,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var location = CreateLocation(warehouse.Id, normalized, now);
        store.AddWarehouse(warehouse);
        store.AddLocation(location);
        await store.SaveChangesAsync(cancellationToken);
        return ToWarehouseResult(new WarehouseView(warehouse, location), []);
    }

    public async Task<WarehouseResult> UpdateWarehouseAsync(Guid warehouseId, SaveWarehouseData data, CancellationToken cancellationToken)
    {
        var normalized = ValidateWarehouse(data, includeCode: false);
        var warehouse = await store.FindWarehouseAsync(warehouseId, cancellationToken)
            ?? throw WarehouseConfigurationOperationException.NotFound("Warehouse was not found.");
        var location = await store.FindWarehouseLocationAsync(warehouseId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        warehouse.Name = normalized.Name;
        warehouse.RegionId = normalized.RegionId;
        warehouse.Address = normalized.Address;
        warehouse.MinimumStockThresholdCartons = normalized.MinimumStockThresholdCartons;
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
            location.Provider = "Manual";
            location.Version++;
            location.NormalizedAt = null;
            location.UpdatedAt = now;
        }

        await store.SaveChangesAsync(cancellationToken);
        var keepers = await store.ListActiveWarehouseKeepersAsync([warehouseId], cancellationToken);
        return ToWarehouseResult(new WarehouseView(warehouse, location), keepers);
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
        var history = await store.ListTransitRulesAsync(warehouseId, true, cancellationToken);
        var existing = history.Where(item => item.IsActive).ToArray();
        var orderedDefinitions = definitions.OrderBy(item => item.MinDistanceMeters).ToArray();
        var orderedExisting = existing.OrderBy(item => item.MinDistanceMeters).ToArray();
        if (orderedExisting.Length == orderedDefinitions.Length && orderedExisting.Zip(orderedDefinitions)
            .All(pair => pair.First.MinDistanceMeters == pair.Second.MinDistanceMeters &&
                pair.First.MaxDistanceMeters == pair.Second.MaxDistanceMeters &&
                pair.First.HandlingMinutes == pair.Second.HandlingMinutes &&
                pair.First.TransitMinutes == pair.Second.TransitMinutes &&
                pair.First.EstimatedTransportCost == pair.Second.EstimatedTransportCost &&
                pair.First.MaxServingDistanceMeters == pair.Second.MaxServingDistanceMeters))
            return orderedExisting.Select(ToTransitResult).ToArray();
        var version = history.Count == 0 ? 1 : history.Max(item => item.Version) + 1;
        foreach (var rule in existing)
        {
            rule.IsActive = false;
            rule.EffectiveTo = now;
            rule.UpdatedAt = now;
        }

        var rules = orderedDefinitions.Select(item => new TransitTimeRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            MinDistanceMeters = item.MinDistanceMeters,
            MaxDistanceMeters = item.MaxDistanceMeters,
            HandlingMinutes = item.HandlingMinutes,
            TransitMinutes = item.TransitMinutes,
            EstimatedTransportCost = item.EstimatedTransportCost,
            MaxServingDistanceMeters = item.MaxServingDistanceMeters,
            Version = version,
            EffectiveFrom = now,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        }).ToArray();
        store.AddTransitRules(rules);
        await store.SaveChangesAsync(cancellationToken);
        return rules.Select(ToTransitResult).ToArray();
    }

    public async Task<IReadOnlyCollection<MlorResult>> ListMlorAsync(
        Guid? retailerId, Guid? productCategoryId, Guid? productId,
        bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListMlorAsync(retailerId, productCategoryId, productId, includeInactive, cancellationToken))
            .Select(ToMlorResult).ToArray();

    public async Task<MlorResult> SetMlorAsync(
        Guid retailerId, Guid? productCategoryId, Guid? productId,
        decimal minimumLifeRatio, int? minimumLifeHours, Guid updatedByUserId,
        CancellationToken cancellationToken)
    {
        if (productCategoryId.HasValue == productId.HasValue)
            throw WarehouseConfigurationOperationException.Validation("Exactly one Category or SKU MLOR scope is required.");
        if (minimumLifeRatio <= 0 || minimumLifeRatio >= 1)
        {
            throw WarehouseConfigurationOperationException.Validation("Minimum life ratio must be greater than 0 and less than 1.");
        }

        var retailer = await store.FindRetailerAsync(retailerId, cancellationToken)
            ?? throw WarehouseConfigurationOperationException.NotFound("Retailer was not found.");
        Product? product = null;
        ProductCategory? category = null;
        if (productId.HasValue)
        {
            product = await store.FindProductAsync(productId.Value, cancellationToken)
                ?? throw WarehouseConfigurationOperationException.NotFound("Product was not found.");
            if (minimumLifeHours is < 0 || minimumLifeHours > product.ShelfLifeHours)
                throw WarehouseConfigurationOperationException.Validation("Minimum life hours must be between 0 and the product shelf life.");
        }
        else
        {
            category = await store.FindCategoryAsync(productCategoryId!.Value, cancellationToken)
                ?? throw WarehouseConfigurationOperationException.NotFound("Category was not found.");
            if (minimumLifeHours.HasValue)
                throw WarehouseConfigurationOperationException.Validation("Minimum life hours can only be set for an SKU scope.");
        }

        var now = timeProvider.GetUtcNow();
        var history = await store.ListMlorAsync(retailerId, productCategoryId, productId, true, cancellationToken);
        var current = await store.FindActiveMlorAsync(retailerId, productCategoryId, productId, cancellationToken);
        if (current is not null)
        {
            current.IsActive = false;
            current.EffectiveTo = now;
        }

        var mlor = new RetailerProductMlor
        {
            Id = Guid.NewGuid(),
            RetailerId = retailerId,
            ProductCategoryId = productCategoryId,
            ProductId = productId,
            MinimumLifeRatio = minimumLifeRatio,
            MinimumLifeHours = minimumLifeHours,
            Version = history.Count == 0 ? 1 : history.Max(item => item.Mlor.Version) + 1,
            UpdatedByUserId = updatedByUserId,
            EffectiveFrom = now,
            IsActive = true
        };
        store.AddMlor(mlor);
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = updatedByUserId,
            EntityName = nameof(RetailerProductMlor),
            EntityId = mlor.Id,
            Action = "RetailerMlorRuleVersionCreated",
            OldValues = current is null ? null : JsonSerializer.Serialize(new
            {
                current.Id,
                current.MinimumLifeRatio,
                current.MinimumLifeHours,
                current.Version
            }),
            NewValues = JsonSerializer.Serialize(new
            {
                mlor.RetailerId,
                mlor.ProductCategoryId,
                mlor.ProductId,
                mlor.MinimumLifeRatio,
                mlor.MinimumLifeHours,
                mlor.Version
            }),
            CreatedAt = now
        });
        await store.SaveChangesAsync(cancellationToken);
        return ToMlorResult(new MlorView(mlor, retailer.Code, retailer.Name,
            category?.Code, category?.Name, product?.Sku, product?.Name));
    }

    public async Task DeactivateMlorAsync(
        Guid mlorId, Guid updatedByUserId, CancellationToken cancellationToken)
    {
        var mlor = await store.FindMlorAsync(mlorId, cancellationToken)
            ?? throw WarehouseConfigurationOperationException.NotFound("MLOR configuration was not found.");
        if (!mlor.IsActive)
            return;

        var now = timeProvider.GetUtcNow();
        mlor.IsActive = false;
        mlor.EffectiveTo = now;
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = updatedByUserId,
            EntityName = nameof(RetailerProductMlor),
            EntityId = mlor.Id,
            Action = "RetailerMlorRuleDeactivated",
            OldValues = JsonSerializer.Serialize(new
            {
                mlor.RetailerId,
                mlor.ProductCategoryId,
                mlor.ProductId,
                mlor.MinimumLifeRatio,
                mlor.MinimumLifeHours,
                mlor.Version
            }),
            NewValues = JsonSerializer.Serialize(new { mlor.IsActive, mlor.EffectiveTo }),
            CreatedAt = now
        });
        await store.SaveChangesAsync(cancellationToken);
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
        if (includeCode && !WarehouseCodeRegex().IsMatch(code))
            throw WarehouseConfigurationOperationException.Validation("Warehouse code must start with WH- and contain uppercase letters, digits, or hyphens.");
        if (name.Length is < 2 or > 150) throw WarehouseConfigurationOperationException.Validation("Warehouse name must contain 2 to 150 characters.");
        if (data.RegionId == Guid.Empty) throw WarehouseConfigurationOperationException.Validation("Region is required.");
        if (address.Length is < 5 or > 500) throw WarehouseConfigurationOperationException.Validation("Warehouse address must contain 5 to 500 characters.");
        if (data.Latitude is < -90 or > 90 || data.Longitude is < -180 or > 180)
            throw WarehouseConfigurationOperationException.Validation("Warehouse coordinates are outside the valid latitude or longitude range.");
        if (data.ProviderPlaceId?.Length > 200) throw WarehouseConfigurationOperationException.Validation("Provider place ID cannot exceed 200 characters.");
        if (data.MinimumStockThresholdCartons < 0)
            throw WarehouseConfigurationOperationException.Validation("Minimum stock threshold must be zero or a positive carton quantity.");
        return data with { Code = code, Name = name, Address = address, ProviderPlaceId = data.ProviderPlaceId?.Trim() };
    }

    private static void ValidateTransitRules(IReadOnlyCollection<TransitTimeRuleData> definitions)
    {
        if (definitions.Count == 0) throw WarehouseConfigurationOperationException.Validation("At least one transit-time rule is required.");
        var ordered = definitions.OrderBy(item => item.MinDistanceMeters).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var rule = ordered[index];
            if (rule.MinDistanceMeters < 0 || rule.MaxDistanceMeters <= rule.MinDistanceMeters ||
                rule.HandlingMinutes < 0 || rule.TransitMinutes <= 0 || rule.EstimatedTransportCost < 0 ||
                rule.MaxServingDistanceMeters < rule.MaxDistanceMeters)
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
            case BusinessConfigurationKeys.DefaultMlorRatio:
                if (!value.TryGetDecimal(out var ratio) || ratio <= 0 || ratio >= 1)
                    throw WarehouseConfigurationOperationException.Validation("Default MLOR ratio must be greater than 0 and less than 1.");
                break;
            case BusinessConfigurationKeys.EtaSchedule:
                if (value.ValueKind != JsonValueKind.Object ||
                    !TryGetLocalTime(value, "allocationRunLocalTime", out var runAt) ||
                    !TryGetLocalTime(value, "planApprovalDeadlineLocalTime", out var approveBy) ||
                    approveBy <= runAt ||
                    !value.TryGetProperty("dispatchSlotsLocalTime", out var slots) ||
                    slots.ValueKind != JsonValueKind.Array || slots.GetArrayLength() == 0)
                    throw WarehouseConfigurationOperationException.Validation(
                        "ETA schedule needs a run time, a later approval deadline, and at least one dispatch slot.");
                break;
            case BusinessConfigurationKeys.SalesKpi:
                if (value.ValueKind != JsonValueKind.Object ||
                    !TryGetDecimal(value, "successfulOrdersTarget", out var orderTarget) || orderTarget <= 0 ||
                    !TryGetDecimal(value, "retailersServedTarget", out var retailerTarget) || retailerTarget <= 0 ||
                    !TryGetDecimal(value, "newRetailersTarget", out var newRetailerTarget) || newRetailerTarget <= 0 ||
                    !TryGetDecimal(value, "promotionRevenueRatioMaximum", out var promotionMaximum) || promotionMaximum <= 0 || promotionMaximum >= 1 ||
                    !TryGetDecimal(value, "successfulOrdersWeight", out var orderWeight) || orderWeight < 0 ||
                    !TryGetDecimal(value, "retailersServedWeight", out var retailerWeight) || retailerWeight < 0 ||
                    !TryGetDecimal(value, "newRetailersWeight", out var newRetailerWeight) || newRetailerWeight < 0 ||
                    !TryGetDecimal(value, "promotionRevenueRatioWeight", out var promotionWeight) || promotionWeight < 0 ||
                    Math.Abs(orderWeight + retailerWeight + newRetailerWeight + promotionWeight - 1m) > 0.0001m)
                    throw WarehouseConfigurationOperationException.Validation(
                        "Sales KPI targets must be positive, the promotion ratio must be between 0 and 1, and weights must be non-negative and sum to 1.");
                break;
        }

        return JsonSerializer.Serialize(value);
    }

    private static bool TryGetLocalTime(JsonElement element, string name, out TimeOnly value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            TimeOnly.TryParse(property.GetString(), out value);
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
        Provider = "Manual",
        Version = 1,
        CreatedAt = now,
        UpdatedAt = now
    };

    private async Task<Dictionary<Guid, IReadOnlyCollection<WarehouseKeeperView>>> ListWarehouseKeeperLookupAsync(
        IEnumerable<Guid> warehouseIds, CancellationToken cancellationToken)
    {
        var ids = warehouseIds.Distinct().ToArray();
        var keepers = await store.ListActiveWarehouseKeepersAsync(ids, cancellationToken);
        return keepers.GroupBy(item => item.WarehouseId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<WarehouseKeeperView>)group.ToArray());
    }

    private static WarehouseResult ToWarehouseResult(
        WarehouseView view, IReadOnlyCollection<WarehouseKeeperView> keepers) => new(
        view.Warehouse.Id, view.Warehouse.Code, view.Warehouse.Name, view.Warehouse.RegionId,
        view.Warehouse.Address, view.Location?.Latitude ?? 0, view.Location?.Longitude ?? 0,
        view.Location?.ProviderPlaceId, view.Warehouse.MinimumStockThresholdCartons,
        keepers.Select(item => new WarehouseKeeperResult(item.UserId, item.FullName, item.Email)).ToArray(),
        view.Warehouse.IsActive, view.Warehouse.CreatedAt, view.Warehouse.UpdatedAt);

    private static TransitTimeRuleResult ToTransitResult(TransitTimeRule rule) => new(
        rule.Id, rule.WarehouseId, rule.MinDistanceMeters, rule.MaxDistanceMeters,
        rule.HandlingMinutes, rule.TransitMinutes, rule.EstimatedTransportCost,
        rule.MaxServingDistanceMeters,
        rule.Version, rule.EffectiveFrom, rule.EffectiveTo,
        rule.IsActive, rule.CreatedAt, rule.UpdatedAt);

    private static MlorResult ToMlorResult(MlorView view) => new(
        view.Mlor.Id, view.Mlor.RetailerId, view.RetailerCode, view.RetailerName,
        view.Mlor.ProductCategoryId, view.CategoryCode, view.CategoryName,
        view.Mlor.ProductId, view.ProductSku, view.ProductName, view.Mlor.MinimumLifeRatio,
        view.Mlor.MinimumLifeHours, view.Mlor.Version, view.Mlor.UpdatedByUserId,
        view.Mlor.EffectiveFrom, view.Mlor.EffectiveTo, view.Mlor.IsActive);

    private static ConfigurationResult ToConfigurationResult(SystemConfiguration item)
    {
        using var document = JsonDocument.Parse(item.ConfigValue);
        return new(item.Id, item.ConfigKey, document.RootElement.Clone(), item.Description, item.ValueType,
            item.Version, item.IsActive, item.UpdatedByUserId, item.EffectiveFrom);
    }

    [GeneratedRegex("^WH-[A-Z0-9]+(?:-[A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex WarehouseCodeRegex();
}
