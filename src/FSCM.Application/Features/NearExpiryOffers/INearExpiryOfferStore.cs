using FSCM.Domain.Entities;

namespace FSCM.Application.Features.NearExpiryOffers;

/// <summary>
/// Product and MLOR reads a line needs. <c>RetailerMlorRatio</c> is the active
/// retailer-SKU ratio; <c>FallbackMlorRatio</c> is the business configuration value
/// <c>mlor.default_ratio</c> used when the pair has none.
/// </summary>
public sealed record OrderLineProductView(
    Product Product,
    decimal? RetailerMlorRatio,
    decimal FallbackMlorRatio);

public interface INearExpiryOfferStore
{
    Task<OrderLineProductView?> FindOrderLineProductAsync(
        Guid retailerId, Guid productId, DateTimeOffset at, CancellationToken cancellationToken);

    Task<bool> RetailerExistsAsync(Guid retailerId, CancellationToken cancellationToken);
}
