using FSCM.Application.Features.Promotions;

namespace FSCM.Application.Features.Orders;

public sealed record OrderPriceSnapshot(
    decimal UnitPrice,
    decimal GrossAmount,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetAmount,
    Guid ProductPriceId,
    DateTimeOffset ProductPriceEffectiveFrom,
    DateTimeOffset? ProductPriceEffectiveTo,
    Guid? PromotionId,
    string? DiscountSourceType,
    string? DiscountSourceCode,
    string? PromotionName,
    decimal? PromotionMinimumQuantity,
    DateTimeOffset? PromotionEffectiveFrom,
    DateTimeOffset? PromotionEffectiveTo);

public interface IOrderPricingService
{
    Task<OrderPriceSnapshot> CalculateAsync(
        Guid productId,
        int quantity,
        Guid retailerId,
        DateTimeOffset pricedAt,
        CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<Guid, OrderPriceSnapshot>> CalculateAsync(
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines,
        Guid retailerId, DateTimeOffset pricedAt, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, OrderPriceSnapshot>();
        foreach (var line in lines)
            result[line.ProductId] = await CalculateAsync(line.ProductId, line.Quantity, retailerId, pricedAt, cancellationToken);
        return result;
    }
}

public sealed class OrderPricingService(PromotionService promotionService) : IOrderPricingService
{
    public async Task<OrderPriceSnapshot> CalculateAsync(
        Guid productId,
        int quantity,
        Guid retailerId,
        DateTimeOffset pricedAt,
        CancellationToken cancellationToken)
    {
        var result = await promotionService.CalculateIndicativePriceAsync(
            productId, quantity, retailerId, pricedAt, cancellationToken);
        return new(
            result.UnitPrice,
            result.GrossAmount,
            result.DiscountPercent,
            result.DiscountAmount,
            result.NetAmount,
            result.ProductPriceId,
            result.ProductPriceEffectiveFrom,
            result.ProductPriceEffectiveTo,
            result.AppliedPromotion?.PromotionId,
            result.AppliedPromotion?.PromotionType,
            result.AppliedPromotion?.Code,
            result.AppliedPromotion?.Name,
            result.AppliedPromotion?.MinimumQuantity,
            result.AppliedPromotion?.EffectiveFrom,
            result.AppliedPromotion?.EffectiveTo);
    }

    public async Task<IReadOnlyDictionary<Guid, OrderPriceSnapshot>> CalculateAsync(
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines,
        Guid retailerId, DateTimeOffset pricedAt, CancellationToken cancellationToken)
    {
        var results = await promotionService.CalculateIndicativePricesAsync(
            lines.Select(item => new PromotionPriceLineInput(item.ProductId, item.Quantity)).ToArray(),
            retailerId, pricedAt, cancellationToken);
        return results.ToDictionary(item => item.Key, item => ToSnapshot(item.Value));
    }

    private static OrderPriceSnapshot ToSnapshot(PriceSnapshotResult result) => new(
        result.UnitPrice, result.GrossAmount, result.DiscountPercent, result.DiscountAmount,
        result.NetAmount, result.ProductPriceId, result.ProductPriceEffectiveFrom,
        result.ProductPriceEffectiveTo, result.AppliedPromotion?.PromotionId,
        result.AppliedPromotion?.PromotionType, result.AppliedPromotion?.Code,
        result.AppliedPromotion?.Name, result.AppliedPromotion?.MinimumQuantity,
        result.AppliedPromotion?.EffectiveFrom, result.AppliedPromotion?.EffectiveTo);
}
