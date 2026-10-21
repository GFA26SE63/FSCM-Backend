using FSCM.Application.Features.Promotions;

namespace FSCM.Application.Features.Orders;

public sealed record OrderPriceSnapshot(
    decimal UnitPrice,
    decimal GrossAmount,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetAmount,
    Guid? PromotionId,
    string? DiscountSourceType,
    string? DiscountSourceCode);

public interface IOrderPricingService
{
    Task<OrderPriceSnapshot> CalculateAsync(
        Guid productId,
        int quantity,
        Guid retailerId,
        DateTimeOffset pricedAt,
        CancellationToken cancellationToken);
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
            result.AppliedPromotion?.PromotionId,
            result.AppliedPromotion?.PromotionType,
            result.AppliedPromotion?.Code);
    }
}
