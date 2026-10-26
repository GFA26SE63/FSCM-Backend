using FSCM.Application.Features.ShelfLifeDiscounts;

namespace FSCM.Application.Features.AllocationRuns;

/// <summary>One allocated part to price: a quantity of one batch serving one order line.</summary>
public sealed record PricingPart(
    Guid RunLineId,
    Guid BatchId,
    Guid WarehouseId,
    Guid ProductId,
    Guid ProductCategoryId,
    string Sku,
    decimal Quantity,
    DateTimeOffset BatchExpiresAt,
    int TotalShelfLifeHours,
    decimal ListUnitPrice,
    decimal BaseNetUnitPrice);

public sealed record PricingOrder(
    Guid RunOrderId,
    string OrderCode,
    IReadOnlyList<PricingPart> Parts);

public sealed record PricedPart(
    Guid RunLineId,
    Guid BatchId,
    Guid WarehouseId,
    string Sku,
    decimal Quantity,
    DateTimeOffset BatchExpiresAt,
    int TotalShelfLifeHours,
    decimal ListUnitPrice,
    decimal BaseNetUnitPrice,
    decimal RemainingPercent,
    Guid? TierId,
    string? DiscountType,
    decimal? DiscountValue,
    decimal ShelfLifeDiscountPerUnit,
    decimal FinalNetUnitPrice,
    decimal Amount,
    int DiscountTableVersion,
    string Explanation);

public sealed record PricedOrder(
    Guid RunOrderId,
    string OrderCode,
    IReadOnlyList<PricedPart> Parts,
    decimal BaseAmount,
    decimal ShelfLifeDiscountAmount,
    decimal FinalAmount);

public sealed record AllocationPricing(
    DateTimeOffset PricedAt,
    IReadOnlyList<PricedOrder> Orders);

/// <summary>
/// Prices a plan once its batches are settled. Every part of the plan version is measured from one
/// shared pricing moment, not from each part's own arrival: two parts of the same order would
/// otherwise drift apart as the run takes time to finish, and the same plan priced twice would not
/// match itself.
///
/// The reduction comes from exactly one shelf-life tier per part. Tiers never stack, so a part
/// cannot collect both a SKU band and a Category band, and a part that matches nothing is simply
/// not reduced.
/// </summary>
public sealed class AllocationPricingService(ShelfLifeDiscountService discounts)
{
    public async Task<AllocationPricing> PriceAsync(
        DateTimeOffset pricedAt,
        IReadOnlyList<PricingOrder> orders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var priced = new List<PricedOrder>();

        foreach (var order in orders)
        {
            var parts = new List<PricedPart>();
            foreach (var part in order.Parts)
            {
                if (part.TotalShelfLifeHours <= 0)
                {
                    throw AllocationRunOperationException.Validation(
                        $"Product {part.Sku} has no usable shelf life, so a remaining percentage cannot be measured.");
                }

                // Measured from the shared moment, so the whole plan version agrees on how near
                // expiry each batch is.
                var remainingHours = (decimal)(part.BatchExpiresAt - pricedAt).TotalHours;
                // Bounded at both ends on purpose. Past its date a batch is at 0, and a batch with
                // more life left than the product's own total shelf life is simply as fresh as that
                // product gets, which is 100 - there is no such thing as 2228% of a shelf life, and
                // the discount bands this feeds only run from 0 to 100 anyway. Unbounded, a single
                // batch whose expiry disagrees with its product's shelf life overflowed
                // ALLOCATION_PLAN_PRICE.RemainingShelfLifePercent, numeric(5,2), and took the whole
                // run down with it.
                var remainingPercent = Math.Clamp(
                    Math.Round(remainingHours / part.TotalShelfLifeHours * 100m, 2,
                        MidpointRounding.AwayFromZero),
                    0m,
                    100m);

                var match = await discounts.MatchAsync(
                    part.ProductId, part.ProductCategoryId, remainingPercent,
                    part.BaseNetUnitPrice, pricedAt, cancellationToken);

                var finalUnitPrice = part.BaseNetUnitPrice - match.DiscountAmountPerUnit;
                parts.Add(new PricedPart(
                    part.RunLineId,
                    part.BatchId,
                    part.WarehouseId,
                    part.Sku,
                    part.Quantity,
                    part.BatchExpiresAt,
                    part.TotalShelfLifeHours,
                    part.ListUnitPrice,
                    part.BaseNetUnitPrice,
                    remainingPercent,
                    match.Tier?.Id,
                    match.Tier?.DiscountType,
                    match.Tier?.DiscountValue,
                    match.DiscountAmountPerUnit,
                    finalUnitPrice,
                    Math.Round(finalUnitPrice * part.Quantity, 2, MidpointRounding.AwayFromZero),
                    match.Version,
                    match.Explanation));
            }

            priced.Add(new PricedOrder(
                order.RunOrderId,
                order.OrderCode,
                parts,
                Math.Round(parts.Sum(item => item.BaseNetUnitPrice * item.Quantity), 2, MidpointRounding.AwayFromZero),
                Math.Round(parts.Sum(item => item.ShelfLifeDiscountPerUnit * item.Quantity), 2, MidpointRounding.AwayFromZero),
                parts.Sum(item => item.Amount)));
        }

        return new AllocationPricing(pricedAt, priced);
    }
}
