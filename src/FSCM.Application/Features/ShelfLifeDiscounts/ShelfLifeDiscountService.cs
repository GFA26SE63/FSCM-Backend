using System.Text.Json;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.ShelfLifeDiscounts;

public static class ShelfLifeDiscountAuditActions
{
    public const string Published = "Published";
}

/// <summary>
/// The near-expiry reduction table. Admin publishes bands of remaining shelf life and what each
/// band takes off; F89 reads one band per allocated part after the batches are fixed.
///
/// The table deliberately holds no MLOR. How little life a customer will accept and how much the
/// price drops are separate decisions, and deriving one from the other would let a discount quietly
/// lower what a retailer is willing to receive.
/// </summary>
public sealed class ShelfLifeDiscountService(IShelfLifeDiscountStore store, TimeProvider timeProvider)
{
    private const string EntityName = nameof(ShelfLifeDiscountTable);

    public async Task<ShelfLifeDiscountTableResult> GetCurrentAsync(CancellationToken cancellationToken) =>
        ToResult(await store.FindCurrentAsync(cancellationToken)
            ?? throw ShelfLifeDiscountOperationException.NotFound(
                "No shelf-life discount table has been published yet."));

    public async Task<ShelfLifeDiscountTableResult> GetVersionAsync(int version, CancellationToken cancellationToken) =>
        ToResult(await store.FindByVersionAsync(version, cancellationToken)
            ?? throw ShelfLifeDiscountOperationException.NotFound(
                $"Shelf-life discount table version {version} was not found."));

    public async Task<IReadOnlyCollection<ShelfLifeDiscountTableResult>> ListHistoryAsync(
        CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).Select(ToResult).ToArray();

    /// <summary>
    /// The one tier that applies to a product at a given remaining percentage, read from the
    /// version in force at <paramref name="at"/>. A SKU rule wins over a Category rule; nothing
    /// matching means no reduction.
    /// </summary>
    public async Task<ShelfLifeDiscountMatch> MatchAsync(
        Guid productId,
        Guid productCategoryId,
        decimal remainingPercent,
        decimal baseNetUnitPrice,
        DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        if (remainingPercent < 0)
        {
            throw ShelfLifeDiscountOperationException.Validation("Remaining percent cannot be negative.");
        }

        if (baseNetUnitPrice < 0)
        {
            throw ShelfLifeDiscountOperationException.Validation("Base price cannot be negative.");
        }

        var view = at is { } moment
            ? await store.FindEffectiveAtAsync(moment, cancellationToken)
                ?? throw ShelfLifeDiscountOperationException.NotFound(
                    $"No shelf-life discount table was in force at {moment:O}.")
            : await store.FindCurrentAsync(cancellationToken)
                ?? throw ShelfLifeDiscountOperationException.NotFound(
                    "No shelf-life discount table has been published yet.");

        var applicable = view.Tiers
            .Where(tier => remainingPercent >= tier.MinRemainingPercent
                && remainingPercent < tier.MaxRemainingPercent)
            .ToArray();

        var tier = applicable.FirstOrDefault(item =>
                item.Scope == ShelfLifeDiscountScopes.Sku && item.ProductId == productId)
            ?? applicable.FirstOrDefault(item =>
                item.Scope == ShelfLifeDiscountScopes.Category && item.ProductCategoryId == productCategoryId);

        if (tier is null)
        {
            return new ShelfLifeDiscountMatch(view.Table.Id, view.Table.Version, remainingPercent, null, 0m,
                $"Không có bậc nào phủ {remainingPercent:0.##}% hạn còn; giảm HSD bằng 0.");
        }

        var discountPerUnit = tier.DiscountType == ShelfLifeDiscountTypes.Percent
            ? Math.Round(baseNetUnitPrice * tier.DiscountValue / 100m, 2, MidpointRounding.AwayFromZero)
            : tier.DiscountValue;

        // The reduction never turns a price negative; a fixed amount larger than the price takes it
        // to zero instead.
        discountPerUnit = Math.Min(discountPerUnit, baseNetUnitPrice);

        return new ShelfLifeDiscountMatch(
            view.Table.Id,
            view.Table.Version,
            remainingPercent,
            ToTierResult(tier),
            discountPerUnit,
            $"Bậc {tier.MinRemainingPercent:0.##}–{tier.MaxRemainingPercent:0.##}% theo {tier.Scope}, " +
            $"giảm {tier.DiscountValue:0.##} kiểu {tier.DiscountType}.");
    }

    public async Task<ShelfLifeDiscountTableResult> PublishAsync(
        PublishShelfLifeDiscountTableData data, Guid actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        var tiers = Validate(data);
        var now = timeProvider.GetUtcNow();

        var current = await store.FindCurrentForUpdateAsync(cancellationToken);
        if (current is not null)
        {
            if (data.EffectiveFrom <= current.EffectiveFrom)
            {
                throw ShelfLifeDiscountOperationException.Conflict(
                    "A new discount table must take effect after the current one.");
            }

            current.IsActive = false;
            current.EffectiveTo = data.EffectiveFrom;
            current.UpdatedAt = now;
        }

        var table = new ShelfLifeDiscountTable
        {
            Id = Guid.NewGuid(),
            Version = await store.MaxVersionAsync(cancellationToken) + 1,
            EffectiveFrom = data.EffectiveFrom,
            EffectiveTo = null,
            IsActive = true,
            Note = data.Note,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddTable(table);
        store.AddTiers(tiers.Select(item => new ShelfLifeDiscountTier
        {
            Id = Guid.NewGuid(),
            ShelfLifeDiscountTableId = table.Id,
            Scope = item.Scope,
            ProductId = item.ProductId,
            ProductCategoryId = item.ProductCategoryId,
            MinRemainingPercent = item.MinRemainingPercent,
            MaxRemainingPercent = item.MaxRemainingPercent,
            DiscountType = item.DiscountType,
            DiscountValue = item.DiscountValue,
            CreatedAt = now
        }));

        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = actorUserId,
            EntityName = EntityName,
            EntityId = table.Id,
            Action = ShelfLifeDiscountAuditActions.Published,
            OldValues = current is null ? null : JsonSerializer.Serialize(new { current.Version, current.EffectiveFrom }),
            NewValues = JsonSerializer.Serialize(new { table.Version, table.EffectiveFrom, TierCount = tiers.Count }),
            Reason = data.Note,
            CreatedAt = now
        });

        await store.SaveChangesAsync(cancellationToken);
        return await GetVersionAsync(table.Version, cancellationToken);
    }

    /// <summary>
    /// Bands may not overlap inside one scope key, so a percentage resolves to exactly one band.
    /// Bands for different SKUs, or for a SKU and a Category, may overlap freely: that is the
    /// precedence rule, not a conflict.
    /// </summary>
    private static IReadOnlyList<ShelfLifeDiscountTierData> Validate(PublishShelfLifeDiscountTableData data)
    {
        if (data.Tiers.Count == 0)
        {
            throw ShelfLifeDiscountOperationException.Validation("A discount table needs at least one tier.");
        }

        foreach (var tier in data.Tiers)
        {
            if (tier.Scope is not (ShelfLifeDiscountScopes.Sku or ShelfLifeDiscountScopes.Category))
            {
                throw ShelfLifeDiscountOperationException.Validation($"Scope '{tier.Scope}' is not supported.");
            }

            if (tier.Scope == ShelfLifeDiscountScopes.Sku && tier.ProductId is null)
            {
                throw ShelfLifeDiscountOperationException.Validation("A SKU tier needs a product.");
            }

            if (tier.Scope == ShelfLifeDiscountScopes.Category && tier.ProductCategoryId is null)
            {
                throw ShelfLifeDiscountOperationException.Validation("A Category tier needs a category.");
            }

            if (tier.DiscountType is not (ShelfLifeDiscountTypes.Percent or ShelfLifeDiscountTypes.FixedPerUnit))
            {
                throw ShelfLifeDiscountOperationException.Validation(
                    $"Discount type '{tier.DiscountType}' is not supported.");
            }

            if (tier.DiscountValue < 0)
            {
                throw ShelfLifeDiscountOperationException.Validation("A discount cannot be negative.");
            }

            if (tier.DiscountType == ShelfLifeDiscountTypes.Percent && tier.DiscountValue > 100)
            {
                throw ShelfLifeDiscountOperationException.Validation("A percentage discount cannot exceed 100.");
            }

            if (tier.MinRemainingPercent < 0 || tier.MaxRemainingPercent > 100)
            {
                throw ShelfLifeDiscountOperationException.Validation(
                    "Remaining shelf life bands must stay between 0 and 100 percent.");
            }

            if (tier.MaxRemainingPercent <= tier.MinRemainingPercent)
            {
                throw ShelfLifeDiscountOperationException.Validation(
                    $"Band starting at {tier.MinRemainingPercent:0.##}% must end after it starts.");
            }
        }

        foreach (var group in data.Tiers.GroupBy(item => (item.Scope, item.ProductId, item.ProductCategoryId)))
        {
            var ordered = group.OrderBy(item => item.MinRemainingPercent).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].MinRemainingPercent < ordered[index - 1].MaxRemainingPercent)
                {
                    throw ShelfLifeDiscountOperationException.Validation(
                        $"Bands overlap between {ordered[index].MinRemainingPercent:0.##}% and " +
                        $"{ordered[index - 1].MaxRemainingPercent:0.##}% for the same scope.");
                }
            }
        }

        return data.Tiers.ToArray();
    }

    private static ShelfLifeDiscountTableResult ToResult(ShelfLifeDiscountTableView view) => new(
        view.Table.Id, view.Table.Version, view.Table.EffectiveFrom, view.Table.EffectiveTo,
        view.Table.IsActive, view.Table.Note, view.Table.CreatedByUserId,
        view.Table.CreatedAt, view.Table.UpdatedAt,
        view.Tiers.OrderBy(item => item.Scope, StringComparer.Ordinal)
            .ThenBy(item => item.MinRemainingPercent)
            .Select(ToTierResult)
            .ToArray());

    private static ShelfLifeDiscountTierResult ToTierResult(ShelfLifeDiscountTier tier) => new(
        tier.Id, tier.Scope, tier.ProductId, tier.ProductCategoryId,
        tier.MinRemainingPercent, tier.MaxRemainingPercent, tier.DiscountType, tier.DiscountValue);
}
