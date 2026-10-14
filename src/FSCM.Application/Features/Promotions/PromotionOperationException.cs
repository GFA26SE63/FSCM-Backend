namespace FSCM.Application.Features.Promotions;

public sealed class PromotionOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static PromotionOperationException Validation(string message) => new("validation_error", message);
    public static PromotionOperationException NotFound(string message) => new("not_found", message);
    public static PromotionOperationException Conflict(string message) => new("conflict", message);
}
