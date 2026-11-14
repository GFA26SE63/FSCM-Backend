using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Catalog;

/// <summary>
/// Category-level default MLOR ("MLOR theo danh mục sản phẩm"). Precedence when an order is
/// placed: retailer SKU rule → retailer Category rule → category default (nearest ancestor that
/// has one) → system default <c>mlor.default_ratio</c>.
/// </summary>
public static class CategoryDefaultMlor
{
    /// <summary>Order-line MLOR source scope for a category default.</summary>
    public const string Scope = "CategoryDefault";

    /// <summary>
    /// The nearest category, starting at <paramref name="categoryId"/> and walking up the parent
    /// chain, that has a valid default MLOR; null when none has one.
    /// </summary>
    public static ProductCategory? Resolve(
        Guid categoryId, IReadOnlyDictionary<Guid, ProductCategory> categories)
    {
        var visited = new HashSet<Guid>();
        var current = categoryId;
        while (visited.Add(current) && categories.TryGetValue(current, out var category))
        {
            if (category.DefaultMlorRatio is > 0 and < 1 && category.DefaultMlorVersion > 0)
                return category;
            if (!category.ParentCategoryId.HasValue) break;
            current = category.ParentCategoryId.Value;
        }
        return null;
    }
}
