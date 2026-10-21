namespace FSCM.Application.Features.NearExpiryOffers;

public sealed class NearExpiryOfferOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static NearExpiryOfferOperationException Validation(string message) => new("validation_error", message);
    public static NearExpiryOfferOperationException NotFound(string message) => new("not_found", message);
    public static NearExpiryOfferOperationException Conflict(string message) => new("conflict", message);
}
