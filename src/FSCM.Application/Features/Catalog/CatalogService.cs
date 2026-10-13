using System.Text.RegularExpressions;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Catalog;

public sealed partial class CatalogService(ICatalogStore store, TimeProvider timeProvider)
{
    private const int MaximumPageSize = 100;

    public async Task<IReadOnlyCollection<CategoryResult>> ListCategoriesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var categories = await store.ListCategoriesAsync(includeInactive, cancellationToken);
        return categories.Select(ToResult).ToArray();
    }

    public async Task<CategoryResult> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await RequireCategoryAsync(categoryId, cancellationToken);
        return ToResult(category);
    }

    public async Task<CategoryResult> CreateCategoryAsync(
        Guid? parentCategoryId,
        string code,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCategoryCode(code);
        ValidateCategoryFields(normalizedCode, name, description);

        if (await store.FindCategoryByCodeAsync(normalizedCode, cancellationToken) is not null)
        {
            throw CatalogOperationException.Conflict($"Category code '{normalizedCode}' already exists.");
        }

        if (parentCategoryId.HasValue)
        {
            var parent = await RequireCategoryAsync(parentCategoryId.Value, cancellationToken);
            if (!parent.IsActive)
            {
                throw CatalogOperationException.Validation("The parent category must be active.");
            }
        }

        var now = timeProvider.GetUtcNow();
        var category = new ProductCategory
        {
            Id = Guid.NewGuid(),
            ParentCategoryId = parentCategoryId,
            Code = normalizedCode,
            Name = name.Trim(),
            Description = NormalizeOptional(description),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        store.AddCategory(category);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(category);
    }

    public async Task<CategoryResult> UpdateCategoryAsync(
        Guid categoryId,
        Guid? parentCategoryId,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        var category = await RequireCategoryAsync(categoryId, cancellationToken);
        ValidateCategoryFields(category.Code, name, description);

        if (parentCategoryId == categoryId)
        {
            throw CatalogOperationException.Validation("A category cannot be its own parent.");
        }

        if (parentCategoryId.HasValue)
        {
            await EnsureValidParentAsync(categoryId, parentCategoryId.Value, cancellationToken);
        }

        category.ParentCategoryId = parentCategoryId;
        category.Name = name.Trim();
        category.Description = NormalizeOptional(description);
        category.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(category);
    }

    public async Task SetCategoryStatusAsync(
        Guid categoryId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var category = await RequireCategoryAsync(categoryId, cancellationToken);
        if (category.IsActive == isActive)
        {
            return;
        }

        if (isActive && category.ParentCategoryId.HasValue)
        {
            var parent = await RequireCategoryAsync(category.ParentCategoryId.Value, cancellationToken);
            if (!parent.IsActive)
            {
                throw CatalogOperationException.InvalidTransition("Activate the parent category first.");
            }
        }

        if (!isActive)
        {
            if (await store.CategoryHasActiveChildrenAsync(categoryId, cancellationToken))
            {
                throw CatalogOperationException.InvalidTransition(
                    "A category with active child categories cannot be inactivated.");
            }

            if (await store.CategoryHasActiveProductsAsync(categoryId, cancellationToken))
            {
                throw CatalogOperationException.InvalidTransition(
                    "A category with Draft or Published products cannot be inactivated.");
            }
        }

        category.IsActive = isActive;
        category.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<CatalogPage<ProductResult>> ListProductsAsync(
        ProductSearch search,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = NormalizeSearch(search);
        var (items, totalCount) = await store.ListProductsAsync(normalizedSearch, cancellationToken);
        return new CatalogPage<ProductResult>(
            items.Select(ToResult).ToArray(),
            normalizedSearch.Page,
            normalizedSearch.PageSize,
            totalCount);
    }

    public async Task<IReadOnlyCollection<ProductResult>> ListProductsForQueryAsync(
        string? search,
        Guid? categoryId,
        string? status,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeSearch(new ProductSearch(search, categoryId, status, 1, MaximumPageSize));
        var items = await store.ListProductsForQueryAsync(
            new ProductQuery(normalized.Search, normalized.CategoryId, normalized.Status), cancellationToken);
        return items.Select(ToResult).ToArray();
    }

    public async Task<ProductResult> GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await store.FindProductViewAsync(productId, cancellationToken)
            ?? throw CatalogOperationException.NotFound("Product was not found.");
        return ToResult(product);
    }

    public async Task<ProductResult> CreateProductAsync(
        CreateProductData data,
        CancellationToken cancellationToken)
    {
        var sku = NormalizeSku(data.Sku);
        ValidateProductFields(sku, data.Name, data.Unit, data.ShelfLifeHours, data.NearDateThresholdHours);
        ValidatePrice(data.InitialUnitPrice);
        await EnsureSkuAvailableAsync(sku, cancellationToken);

        await RequireActiveCategoryAsync(data.ProductCategoryId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            ProductCategoryId = data.ProductCategoryId,
            Sku = sku,
            Name = data.Name.Trim(),
            Unit = data.Unit.Trim(),
            ShelfLifeHours = data.ShelfLifeHours,
            NearDateThresholdHours = data.NearDateThresholdHours,
            Status = ProductStatuses.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddProduct(product);

        if (data.InitialUnitPrice.HasValue)
        {
            store.AddPrice(CreatePrice(product.Id, data.InitialUnitPrice.Value, now));
        }

        await store.SaveChangesAsync(cancellationToken);
        return await GetProductAsync(product.Id, cancellationToken);
    }

    public async Task<ProductResult> UpdateProductAsync(
        Guid productId,
        UpdateProductData data,
        CancellationToken cancellationToken)
    {
        var product = await RequireProductAsync(productId, cancellationToken);
        EnsureDraft(product, "Only Draft products can be edited.");
        ValidateProductFields(product.Sku, data.Name, data.Unit, data.ShelfLifeHours, data.NearDateThresholdHours);
        await RequireActiveCategoryAsync(data.ProductCategoryId, cancellationToken);

        product.ProductCategoryId = data.ProductCategoryId;
        product.Name = data.Name.Trim();
        product.Unit = data.Unit.Trim();
        product.ShelfLifeHours = data.ShelfLifeHours;
        product.NearDateThresholdHours = data.NearDateThresholdHours;
        product.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return await GetProductAsync(product.Id, cancellationToken);
    }

    public async Task DeleteDraftProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await RequireProductAsync(productId, cancellationToken);
        EnsureDraft(product, "Only Draft products can be deleted.");

        var prices = await store.ListPricesAsync(productId, cancellationToken);
        store.RemovePrices(prices);
        store.RemoveProduct(product);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductResult> PublishProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await RequireProductAsync(productId, cancellationToken);
        EnsureDraft(product, "Only Draft products can be published.");
        await RequireActiveCategoryAsync(product.ProductCategoryId, cancellationToken);

        if (await store.FindActivePriceAsync(productId, cancellationToken) is null)
        {
            throw CatalogOperationException.InvalidTransition("A product requires an active price before publishing.");
        }

        var now = timeProvider.GetUtcNow();
        product.Status = ProductStatuses.Published;
        product.PublishedAt = now;
        product.UpdatedAt = now;

        if (product.ReplacesProductId.HasValue)
        {
            var predecessor = await RequireProductAsync(product.ReplacesProductId.Value, cancellationToken);
            if (predecessor.Status == ProductStatuses.Published)
            {
                predecessor.Status = ProductStatuses.Inactive;
                predecessor.InactivatedAt = now;
                predecessor.UpdatedAt = now;
            }
        }

        await store.SaveChangesAsync(cancellationToken);
        return await GetProductAsync(product.Id, cancellationToken);
    }

    public async Task InactivateProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await RequireProductAsync(productId, cancellationToken);
        if (product.Status != ProductStatuses.Published)
        {
            throw CatalogOperationException.InvalidTransition("Only Published products can be inactivated.");
        }

        var now = timeProvider.GetUtcNow();
        product.Status = ProductStatuses.Inactive;
        product.InactivatedAt = now;
        product.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductResult> CreateReplacementAsync(
        Guid productId,
        CreateReplacementProductData data,
        CancellationToken cancellationToken)
    {
        var predecessor = await RequireProductAsync(productId, cancellationToken);
        if (predecessor.Status != ProductStatuses.Published)
        {
            throw CatalogOperationException.InvalidTransition(
                "A replacement SKU can only be created for a Published product.");
        }

        var sku = NormalizeSku(data.Sku);
        ValidateProductFields(sku, data.Name, data.Unit, data.ShelfLifeHours, data.NearDateThresholdHours);
        ValidatePrice(data.InitialUnitPrice);
        await EnsureSkuAvailableAsync(sku, cancellationToken);

        if (await store.FindReplacementAsync(predecessor.Id, cancellationToken) is not null)
        {
            throw CatalogOperationException.Conflict("A replacement SKU already exists for this product.");
        }

        var now = timeProvider.GetUtcNow();
        var replacement = new Product
        {
            Id = Guid.NewGuid(),
            ProductCategoryId = predecessor.ProductCategoryId,
            ReplacesProductId = predecessor.Id,
            Sku = sku,
            Name = data.Name.Trim(),
            Unit = data.Unit.Trim(),
            ShelfLifeHours = data.ShelfLifeHours,
            NearDateThresholdHours = data.NearDateThresholdHours,
            Status = ProductStatuses.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddProduct(replacement);

        var initialPrice = data.InitialUnitPrice ??
            (await store.FindActivePriceAsync(predecessor.Id, cancellationToken))?.UnitPrice;
        if (initialPrice.HasValue)
        {
            store.AddPrice(CreatePrice(replacement.Id, initialPrice.Value, now));
        }

        await store.SaveChangesAsync(cancellationToken);
        return await GetProductAsync(replacement.Id, cancellationToken);
    }

    public async Task<ProductPriceResult> SetPriceAsync(
        Guid productId,
        decimal unitPrice,
        CancellationToken cancellationToken)
    {
        var product = await RequireProductAsync(productId, cancellationToken);
        if (product.Status == ProductStatuses.Inactive)
        {
            throw CatalogOperationException.InvalidTransition("Inactive products cannot receive new prices.");
        }

        ValidatePrice(unitPrice);
        var now = timeProvider.GetUtcNow();
        var current = await store.FindActivePriceAsync(productId, cancellationToken);
        if (current?.UnitPrice == unitPrice)
        {
            throw CatalogOperationException.Conflict("The supplied price is already active.");
        }

        if (current is not null)
        {
            current.IsActive = false;
            current.EffectiveTo = now;
        }

        var price = CreatePrice(productId, unitPrice, now);
        store.AddPrice(price);
        await store.SaveChangesAsync(cancellationToken);
        return ToResult(price);
    }

    public async Task<IReadOnlyCollection<ProductPriceResult>> ListPriceHistoryAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        await RequireProductAsync(productId, cancellationToken);
        var prices = await store.ListPricesAsync(productId, cancellationToken);
        return prices.Select(ToResult).ToArray();
    }

    public async Task<Dictionary<Guid, ProductPriceResult[]>> ListPriceHistoryByProductIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var prices = await store.ListPricesAsync(productIds.Distinct().ToArray(), cancellationToken);
        return prices.GroupBy(price => price.ProductId)
            .ToDictionary(group => group.Key, group => group.Select(ToResult).ToArray());
    }

    public async Task<ProductPriceSuggestion> GetPriceSuggestionAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var product = await RequireProductAsync(productId, cancellationToken);
        var sourceProductId = product.Id;
        var prices = await store.ListPricesAsync(sourceProductId, cancellationToken);

        if (prices.Count == 0 && product.ReplacesProductId.HasValue)
        {
            sourceProductId = product.ReplacesProductId.Value;
            prices = await store.ListPricesAsync(sourceProductId, cancellationToken);
        }

        var recent = prices.OrderByDescending(price => price.EffectiveFrom).Take(5).ToArray();
        if (recent.Length == 0)
        {
            throw CatalogOperationException.NotFound("No price history is available for this product.");
        }

        return new ProductPriceSuggestion(
            product.Id,
            sourceProductId,
            recent[0].UnitPrice,
            decimal.Round(recent.Average(price => price.UnitPrice), 2),
            recent.Min(price => price.UnitPrice),
            recent.Max(price => price.UnitPrice),
            recent.Length,
            recent[0].EffectiveFrom);
    }

    private async Task EnsureValidParentAsync(
        Guid categoryId,
        Guid parentCategoryId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { categoryId };
        var currentId = parentCategoryId;
        while (true)
        {
            if (!visited.Add(currentId))
            {
                throw CatalogOperationException.Validation("The category relationship would create a cycle.");
            }

            var current = await RequireCategoryAsync(currentId, cancellationToken);
            if (!current.IsActive)
            {
                throw CatalogOperationException.Validation("The parent category must be active.");
            }

            if (!current.ParentCategoryId.HasValue)
            {
                return;
            }

            currentId = current.ParentCategoryId.Value;
        }
    }

    private async Task<ProductCategory> RequireCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindCategoryAsync(id, cancellationToken)
        ?? throw CatalogOperationException.NotFound("Category was not found.");

    private async Task RequireActiveCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await RequireCategoryAsync(id, cancellationToken);
        if (!category.IsActive)
        {
            throw CatalogOperationException.Validation("The product category must be active.");
        }
    }

    private async Task<Product> RequireProductAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindProductAsync(id, cancellationToken)
        ?? throw CatalogOperationException.NotFound("Product was not found.");

    private async Task EnsureSkuAvailableAsync(string sku, CancellationToken cancellationToken)
    {
        if (await store.FindProductBySkuAsync(sku, cancellationToken) is not null)
        {
            throw CatalogOperationException.Conflict($"SKU '{sku}' already exists.");
        }
    }

    private static void EnsureDraft(Product product, string message)
    {
        if (product.Status != ProductStatuses.Draft)
        {
            throw CatalogOperationException.InvalidTransition(message);
        }
    }

    private static ProductSearch NormalizeSearch(ProductSearch search)
    {
        if (search.Page < 1)
        {
            throw CatalogOperationException.Validation("Page must be at least 1.");
        }

        if (search.PageSize is < 1 or > MaximumPageSize)
        {
            throw CatalogOperationException.Validation($"Page size must be between 1 and {MaximumPageSize}.");
        }

        var status = NormalizeOptional(search.Status);
        if (status is not null)
        {
            status = ProductStatuses.All.SingleOrDefault(
                value => value.Equals(status, StringComparison.OrdinalIgnoreCase))
                ?? throw CatalogOperationException.Validation("Status must be Draft, Published, or Inactive.");
        }

        return search with { Search = NormalizeOptional(search.Search), Status = status };
    }

    private static void ValidateCategoryFields(string code, string name, string? description)
    {
        if (!CategoryCodePattern().IsMatch(code))
        {
            throw CatalogOperationException.Validation("Category code must use the CAT-00 format.");
        }

        ValidateRequiredLength(name, 150, "Category name");
        if (description?.Length > 4000)
        {
            throw CatalogOperationException.Validation("Category description cannot exceed 4000 characters.");
        }
    }

    private static void ValidateProductFields(
        string sku,
        string name,
        string unit,
        int shelfLifeHours,
        int nearDateThresholdHours)
    {
        if (!SkuPattern().IsMatch(sku))
        {
            throw CatalogOperationException.Validation("SKU must use the SKU000 format.");
        }

        ValidateRequiredLength(name, 200, "Product name");
        ValidateRequiredLength(unit, 50, "Unit");
        if (shelfLifeHours <= 0)
        {
            throw CatalogOperationException.Validation("Shelf life must be greater than zero hours.");
        }

        if (nearDateThresholdHours < 0 || nearDateThresholdHours >= shelfLifeHours)
        {
            throw CatalogOperationException.Validation(
                "Near-date threshold must be non-negative and less than shelf life.");
        }
    }

    private static void ValidatePrice(decimal? unitPrice)
    {
        if (unitPrice.HasValue && unitPrice.Value <= 0)
        {
            throw CatalogOperationException.Validation("Unit price must be greater than zero.");
        }

        if (unitPrice.HasValue &&
            (unitPrice.Value > 9_999_999_999_999_999.99m || decimal.Round(unitPrice.Value, 2) != unitPrice.Value))
        {
            throw CatalogOperationException.Validation(
                "Unit price cannot exceed 9,999,999,999,999,999.99 and must use at most two decimal places.");
        }
    }

    private static void ValidateRequiredLength(string value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            throw CatalogOperationException.Validation(
                $"{fieldName} is required and cannot exceed {maximumLength} characters.");
        }
    }

    private static ProductPrice CreatePrice(Guid productId, decimal unitPrice, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        UnitPrice = unitPrice,
        EffectiveFrom = now,
        IsActive = true,
        CreatedAt = now
    };

    private static string NormalizeCategoryCode(string code) => code.Trim().ToUpperInvariant();
    private static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();
    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CategoryResult ToResult(ProductCategory category) => new(
        category.Id,
        category.ParentCategoryId,
        category.Code,
        category.Name,
        category.Description,
        category.IsActive,
        category.CreatedAt,
        category.UpdatedAt);

    private static ProductResult ToResult(ProductView view) => new(
        view.Product.Id,
        view.Product.ProductCategoryId,
        view.CategoryCode,
        view.CategoryName,
        view.Product.ReplacesProductId,
        view.Product.Sku,
        view.Product.Name,
        view.Product.Unit,
        view.Product.ShelfLifeHours,
        view.Product.NearDateThresholdHours,
        view.Product.Status,
        view.CurrentUnitPrice,
        view.Product.PublishedAt,
        view.Product.InactivatedAt,
        view.Product.CreatedAt,
        view.Product.UpdatedAt);

    private static ProductPriceResult ToResult(ProductPrice price) => new(
        price.Id,
        price.ProductId,
        price.UnitPrice,
        price.EffectiveFrom,
        price.EffectiveTo,
        price.IsActive,
        price.CreatedAt);

    [GeneratedRegex("^CAT-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CategoryCodePattern();

    [GeneratedRegex("^SKU[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
