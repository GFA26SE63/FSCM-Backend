namespace FSCM.Application.Features.NearExpiryOffers;

/// <summary>
/// Read side of the near-expiry offer catalogue, implemented by the batch-discount module (F46).
/// Nothing here writes: an offer's batches, price, validity window, offer ratio, version, and
/// confirmation are set by Admin in that module.
/// </summary>
public interface INearExpiryOfferCatalog
{
    /// <summary>
    /// The exact version a line was accepted against, whether or not it is still published, so a
    /// synced offline line can be checked against the terms the customer actually saw.
    /// </summary>
    Task<NearExpiryOfferView?> FindOfferVersionAsync(
        Guid offerId, int version, CancellationToken cancellationToken);

    /// <summary>
    /// Confirmed offers a Sales user may show at <paramref name="at"/>. Feeds the mobile offer tab
    /// and the app data snapshot.
    /// </summary>
    Task<IReadOnlyCollection<NearExpiryOfferView>> ListPublishedOffersAsync(
        Guid? productId, Guid? retailerId, DateTimeOffset at, CancellationToken cancellationToken);
}
