namespace FSCM.Application.Features.NearExpiryOffers;

public static class OrderLineTypes
{
    public const string Standard = "Standard";
    public const string NearExpiryOffer = "NearExpiryOffer";
}

public static class MlorSources
{
    /// <summary>Retailer-SKU ratio configured for this retailer and product.</summary>
    public const string RetailerProduct = "RetailerProduct";

    /// <summary>Fallback when the retailer-SKU pair has no active ratio.</summary>
    public const string ProductDefault = "ProductDefault";

    /// <summary>Offer-specific ratio from the accepted offer version.</summary>
    public const string NearExpiryOffer = "NearExpiryOffer";
}

public static class NearExpiryOfferConstants
{
    /// <summary>A ratio is a fraction of total shelf life, strictly between 0 and 1 (FEFO v3 4.7.1).</summary>
    public const decimal MinimumMlorRatioExclusive = 0m;
    public const decimal MaximumMlorRatioExclusive = 1m;
}
