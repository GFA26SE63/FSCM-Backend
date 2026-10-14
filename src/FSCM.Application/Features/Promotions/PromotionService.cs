using System.Text.Json;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Promotions;

public sealed class PromotionService(IPromotionStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyCollection<PromotionResult>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListPromotionsAsync(includeInactive, cancellationToken)).Select(ToResult).ToArray();

    public async Task<PromotionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResult(await store.FindPromotionViewAsync(id, cancellationToken)
            ?? throw PromotionOperationException.NotFound("Promotion was not found."));

    public async Task<PromotionResult> CreateAsync(
        CreatePromotionData data, Guid actorUserId, CancellationToken cancellationToken)
    {
        var normalized = await ValidateCampaignAsync(data.Name, data.MinimumQuantity,
            data.DiscountPercent, data.StartAt, data.EndAt, data.ProductIds,
            data.CategoryIds, data.RetailerIds, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var startAt = data.StartAt.ToUniversalTime();
        var endAt = data.EndAt.ToUniversalTime();
        var promotion = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = await store.NextPromotionCodeAsync(cancellationToken),
            Name = normalized.Name,
            PromotionType = PromotionTypes.Campaign,
            MinimumQuantity = data.MinimumQuantity,
            DiscountPercent = data.DiscountPercent,
            StartAt = startAt,
            EndAt = endAt,
            IsActive = false,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddPromotion(promotion);
        AddScopeLinks(promotion.Id, normalized.ProductIds, normalized.CategoryIds, normalized.RetailerIds);
        AddAudit(promotion.Id, actorUserId, PromotionAuditActions.Created, null,
            new { promotion.Code, promotion.Name, promotion.DiscountPercent, promotion.StartAt, promotion.EndAt }, now);
        await store.SaveChangesAsync(cancellationToken);
        return await GetAsync(promotion.Id, cancellationToken);
    }

    public async Task<PromotionResult> UpdateAsync(
        Guid id, UpdatePromotionData data, Guid actorUserId, CancellationToken cancellationToken)
    {
        var promotion = await store.FindPromotionAsync(id, cancellationToken)
            ?? throw PromotionOperationException.NotFound("Promotion was not found.");
        if (promotion.PromotionType != PromotionTypes.Campaign)
            throw PromotionOperationException.Validation("Batch discounts must be managed through the batch discount API.");
        var normalized = await ValidateCampaignAsync(data.Name, data.MinimumQuantity,
            data.DiscountPercent, data.StartAt, data.EndAt, data.ProductIds,
            data.CategoryIds, data.RetailerIds, cancellationToken);
        var oldProducts = await store.ListPromotionProductsAsync(id, cancellationToken);
        var oldCategories = await store.ListPromotionCategoriesAsync(id, cancellationToken);
        var oldRetailers = await store.ListPromotionRetailersAsync(id, cancellationToken);
        var oldValues = new
        {
            promotion.Name,
            promotion.MinimumQuantity,
            promotion.DiscountPercent,
            promotion.StartAt,
            promotion.EndAt,
            ProductIds = oldProducts.Select(item => item.ProductId),
            CategoryIds = oldCategories.Select(item => item.ProductCategoryId),
            RetailerIds = oldRetailers.Select(item => item.RetailerId)
        };
        promotion.Name = normalized.Name;
        promotion.MinimumQuantity = data.MinimumQuantity;
        promotion.DiscountPercent = data.DiscountPercent;
        promotion.StartAt = data.StartAt.ToUniversalTime();
        promotion.EndAt = data.EndAt.ToUniversalTime();
        promotion.UpdatedAt = timeProvider.GetUtcNow();

        store.RemovePromotionProducts(oldProducts);
        store.RemovePromotionCategories(oldCategories);
        store.RemovePromotionRetailers(oldRetailers);
        AddScopeLinks(id, normalized.ProductIds, normalized.CategoryIds, normalized.RetailerIds);
        AddAudit(id, actorUserId, PromotionAuditActions.Updated, oldValues,
            new
            {
                promotion.Name,
                promotion.MinimumQuantity,
                promotion.DiscountPercent,
                promotion.StartAt,
                promotion.EndAt,
                ProductIds = normalized.ProductIds,
                CategoryIds = normalized.CategoryIds,
                RetailerIds = normalized.RetailerIds
            }, promotion.UpdatedAt);
        await store.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public Task<PromotionResult> ActivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken) =>
        SetCampaignActiveAsync(id, true, actorUserId, cancellationToken);

    public Task<PromotionResult> DeactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken) =>
        SetCampaignActiveAsync(id, false, actorUserId, cancellationToken);

    public async Task<IReadOnlyCollection<BatchDiscountSuggestionResult>> ListBatchSuggestionsAsync(
        Guid? warehouseId, Guid? productId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return (await store.ListBatchDiscountCandidatesAsync(warehouseId, productId, cancellationToken))
            .Where(item => item.AvailableQuantity > 0 && item.Batch.ExpirationDate >= today)
            .Select(item =>
            {
                var remainingDays = item.Batch.ExpirationDate.DayNumber - today.DayNumber;
                var shelfLifeDays = item.Product.ShelfLifeHours / 24m;
                var remainingPercent = shelfLifeDays <= 0 ? 0m :
                    Math.Round(Math.Clamp(remainingDays / shelfLifeDays * 100m, 0m, 100m), 2);
                var suggestion = remainingPercent switch
                {
                    <= 30m => 15m,
                    <= 40m => 10m,
                    <= 60m => 5m,
                    _ => 0m
                };
                return new BatchDiscountSuggestionResult(item.Batch.Id, item.Batch.BatchCode,
                    item.Warehouse.Id, item.Warehouse.Code, item.Product.Id, item.Product.Sku,
                    item.Product.Name, item.Batch.ExpirationDate, item.AvailableQuantity,
                    remainingDays, remainingPercent, suggestion, item.CurrentDiscountPercent);
            })
            .OrderBy(item => item.ExpirationDate)
            .ThenBy(item => item.BatchCode, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<BatchDiscountResult>> ListBatchDiscountsAsync(
        Guid? warehouseId, Guid? productId, bool includeInactive, CancellationToken cancellationToken) =>
        (await store.ListBatchDiscountsAsync(warehouseId, productId, includeInactive, cancellationToken))
        .Select(ToResult).ToArray();

    public async Task<BatchDiscountResult> GetBatchDiscountAsync(Guid id, CancellationToken cancellationToken) =>
        ToResult(await store.FindBatchDiscountAsync(id, cancellationToken)
            ?? throw PromotionOperationException.NotFound("Batch discount was not found."));

    public async Task<BatchDiscountResult> ApplyBatchDiscountAsync(
        Guid batchId, ApplyBatchDiscountData data, Guid actorUserId, CancellationToken cancellationToken)
    {
        ValidateDiscount(data.DiscountPercent, 50m, "Batch discount");
        ValidatePeriod(data.StartAt, data.EndAt);
        var batch = await store.FindBatchAsync(batchId, cancellationToken)
            ?? throw PromotionOperationException.NotFound("Batch was not found.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (batch.ExpirationDate < today)
            throw PromotionOperationException.Conflict("Expired batches cannot receive a discount.");
        var startAt = data.StartAt.ToUniversalTime();
        var endAt = data.EndAt.ToUniversalTime();
        if (await store.HasOverlappingBatchDiscountAsync(batchId, startAt, endAt, null, cancellationToken))
            throw PromotionOperationException.Conflict("The batch already has a discount in the requested validity period.");

        var now = timeProvider.GetUtcNow();
        var promotion = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = await store.NextPromotionCodeAsync(cancellationToken),
            Name = $"Chiết khấu lô {batch.BatchCode}",
            PromotionType = PromotionTypes.BatchDiscount,
            MinimumQuantity = 1m,
            DiscountPercent = data.DiscountPercent,
            StartAt = startAt,
            EndAt = endAt,
            IsActive = true,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        var assignment = new BatchPromotion
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            PromotionId = promotion.Id,
            AssignedByUserId = actorUserId,
            IsActive = true,
            AssignedAt = now
        };
        store.AddPromotion(promotion);
        store.AddBatchPromotion(assignment);
        AddAudit(assignment.Id, actorUserId, PromotionAuditActions.BatchDiscountApplied, null,
            new
            {
                BatchId = batchId,
                promotion.Code,
                promotion.DiscountPercent,
                promotion.StartAt,
                promotion.EndAt
            }, now, "BatchPromotion");
        await store.SaveChangesAsync(cancellationToken);
        return await GetBatchDiscountAsync(assignment.Id, cancellationToken);
    }

    public async Task<BatchDiscountResult> RemoveBatchDiscountAsync(
        Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var view = await store.FindBatchDiscountAsync(id, cancellationToken)
            ?? throw PromotionOperationException.NotFound("Batch discount was not found.");
        if (!view.Assignment.IsActive)
            return ToResult(view);
        var now = timeProvider.GetUtcNow();
        view.Assignment.IsActive = false;
        view.Assignment.RemovedAt = now;
        view.Promotion.IsActive = false;
        view.Promotion.UpdatedAt = now;
        AddAudit(view.Assignment.Id, actorUserId, PromotionAuditActions.BatchDiscountRemoved,
            new { IsActive = true }, new { IsActive = false, RemovedAt = now }, now,
            "BatchPromotion");
        await store.SaveChangesAsync(cancellationToken);
        return await GetBatchDiscountAsync(id, cancellationToken);
    }

    public async Task<PriceSnapshotResult> CalculatePriceAsync(
        Guid productId, decimal quantity, Guid? batchId, Guid? retailerId,
        DateTimeOffset? at, CancellationToken cancellationToken)
    {
        if (quantity <= 0 || quantity != decimal.Truncate(quantity))
            throw PromotionOperationException.Validation("Quantity must be a positive whole number of cartons.");
        var pricedAt = (at ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var productView = await store.FindPriceProductAsync(productId, pricedAt, cancellationToken)
            ?? throw PromotionOperationException.NotFound("Published product or effective price was not found.");
        Batch? batch = null;
        if (batchId.HasValue)
        {
            batch = await store.FindPriceBatchAsync(batchId.Value, cancellationToken)
                ?? throw PromotionOperationException.NotFound("Batch was not found.");
            if (batch.ProductId != productId)
                throw PromotionOperationException.Validation("Batch does not belong to the requested product.");
            if (batch.ExpirationDate < DateOnly.FromDateTime(pricedAt.UtcDateTime))
                throw PromotionOperationException.Conflict("Expired batches cannot be priced for sale.");
        }

        var eligible = new List<AppliedPromotionSnapshot>();
        var campaigns = await store.ListEligibleCampaignsAsync(productId,
            productView.Product.ProductCategoryId, retailerId, quantity, pricedAt, cancellationToken);
        eligible.AddRange(campaigns.Select(item => ToSnapshot(item.Promotion)));
        if (batch is not null)
        {
            var discounts = await store.ListEligibleBatchDiscountsAsync(batch.Id, quantity, pricedAt, cancellationToken);
            eligible.AddRange(discounts.Select(item => ToSnapshot(item.Promotion)));
        }

        // Approved stacking rule: promotions never compound. The highest discount wins;
        // an equal batch-specific discount wins over a campaign, then code is the stable tie-breaker.
        var ordered = eligible.OrderByDescending(item => item.DiscountPercent)
            .ThenByDescending(item => item.PromotionType == PromotionTypes.BatchDiscount)
            .ThenBy(item => item.Code, StringComparer.Ordinal).ToArray();
        var applied = ordered.FirstOrDefault();
        var unitPrice = productView.Price.UnitPrice;
        var gross = Math.Round(unitPrice * quantity, 0, MidpointRounding.AwayFromZero);
        var discountPercent = applied?.DiscountPercent ?? 0m;
        var discountAmount = Math.Round(gross * discountPercent / 100m, 0, MidpointRounding.AwayFromZero);
        var net = gross - discountAmount;
        return new PriceSnapshotResult(productId, productView.Product.Sku, productView.Product.Name,
            batch?.Id, batch?.BatchCode, retailerId, quantity, unitPrice, gross,
            discountPercent, discountAmount, Math.Round(net / quantity, 2), net,
            applied, ordered, pricedAt);
    }

    private async Task<PromotionResult> SetCampaignActiveAsync(
        Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken)
    {
        var promotion = await store.FindPromotionAsync(id, cancellationToken)
            ?? throw PromotionOperationException.NotFound("Promotion was not found.");
        if (promotion.PromotionType != PromotionTypes.Campaign)
            throw PromotionOperationException.Validation("Batch discounts must be managed through the batch discount API.");
        if (isActive && promotion.EndAt < timeProvider.GetUtcNow())
            throw PromotionOperationException.Conflict("An expired promotion cannot be activated.");
        if (promotion.IsActive == isActive)
            return await GetAsync(id, cancellationToken);
        var oldValue = promotion.IsActive;
        promotion.IsActive = isActive;
        promotion.UpdatedAt = timeProvider.GetUtcNow();
        AddAudit(id, actorUserId, isActive ? PromotionAuditActions.Activated : PromotionAuditActions.Deactivated,
            new { IsActive = oldValue }, new { promotion.IsActive }, promotion.UpdatedAt);
        await store.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<(string Name, Guid[] ProductIds, Guid[] CategoryIds, Guid[] RetailerIds)> ValidateCampaignAsync(
        string name, decimal minimumQuantity, decimal discountPercent,
        DateTimeOffset startAt, DateTimeOffset endAt, IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> categoryIds, IReadOnlyCollection<Guid> retailerIds,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 3 or > 200)
            throw PromotionOperationException.Validation("Promotion name must contain between 3 and 200 characters.");
        if (minimumQuantity <= 0 || minimumQuantity != decimal.Truncate(minimumQuantity))
            throw PromotionOperationException.Validation("Minimum quantity must be a positive whole number of cartons.");
        ValidateDiscount(discountPercent, 100m, "Campaign discount");
        ValidatePeriod(startAt, endAt);
        var distinctProducts = productIds.Distinct().ToArray();
        var distinctCategories = categoryIds.Distinct().ToArray();
        var distinctRetailers = retailerIds.Distinct().ToArray();
        if (distinctProducts.Length == 0 && distinctCategories.Length == 0)
            throw PromotionOperationException.Validation("A campaign must target at least one SKU or product category.");
        if ((await store.FindProductsAsync(distinctProducts, cancellationToken)).Count != distinctProducts.Length)
            throw PromotionOperationException.NotFound("One or more campaign products were not found.");
        if ((await store.FindCategoriesAsync(distinctCategories, cancellationToken)).Count != distinctCategories.Length)
            throw PromotionOperationException.NotFound("One or more campaign categories were not found.");
        if ((await store.FindRetailersAsync(distinctRetailers, cancellationToken)).Count != distinctRetailers.Length)
            throw PromotionOperationException.NotFound("One or more campaign retailers were not found.");
        return (normalizedName, distinctProducts, distinctCategories, distinctRetailers);
    }

    private static void ValidateDiscount(decimal discountPercent, decimal maximum, string label)
    {
        if (discountPercent < 1m || discountPercent > maximum)
            throw PromotionOperationException.Validation($"{label} must be between 1% and {maximum}%.");
    }

    private static void ValidatePeriod(DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (endAt <= startAt)
            throw PromotionOperationException.Validation("Validity end must be later than validity start.");
    }

    private void AddScopeLinks(Guid promotionId, IEnumerable<Guid> products,
        IEnumerable<Guid> categories, IEnumerable<Guid> retailers)
    {
        foreach (var id in products)
            store.AddPromotionProduct(new PromotionProduct { Id = Guid.NewGuid(), PromotionId = promotionId, ProductId = id });
        foreach (var id in categories)
            store.AddPromotionCategory(new PromotionCategory { Id = Guid.NewGuid(), PromotionId = promotionId, ProductCategoryId = id });
        foreach (var id in retailers)
            store.AddPromotionRetailer(new PromotionRetailer { Id = Guid.NewGuid(), PromotionId = promotionId, RetailerId = id });
    }

    private void AddAudit(Guid entityId, Guid actorUserId, string action, object? oldValues,
        object? newValues, DateTimeOffset now, string entityName = "Promotion") =>
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = actorUserId,
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
            CreatedAt = now
        });

    private static PromotionResult ToResult(PromotionView view) => new(
        view.Promotion.Id, view.Promotion.Code, view.Promotion.Name,
        view.Promotion.PromotionType, view.Promotion.MinimumQuantity,
        view.Promotion.DiscountPercent, view.Promotion.StartAt, view.Promotion.EndAt,
        view.Promotion.IsActive, view.Promotion.CreatedByUserId, view.Promotion.CreatedAt,
        view.Promotion.UpdatedAt, view.ProductIds, view.CategoryIds, view.RetailerIds);

    private static BatchDiscountResult ToResult(BatchDiscountView view) => new(
        view.Assignment.Id, view.Batch.Id, view.Batch.BatchCode, view.Warehouse.Id,
        view.Warehouse.Code, view.Product.Id, view.Product.Sku, view.Product.Name,
        view.Batch.ExpirationDate, view.Promotion.Id, view.Promotion.Code,
        view.Promotion.DiscountPercent, view.Promotion.StartAt, view.Promotion.EndAt,
        view.Assignment.IsActive && view.Promotion.IsActive, view.Assignment.AssignedByUserId,
        view.Assignment.AssignedAt, view.Assignment.RemovedAt);

    private static AppliedPromotionSnapshot ToSnapshot(Promotion promotion) =>
        new(promotion.Id, promotion.Code, promotion.Name,
            promotion.PromotionType, promotion.DiscountPercent);
}
