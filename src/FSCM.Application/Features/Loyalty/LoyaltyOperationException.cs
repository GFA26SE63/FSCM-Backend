namespace FSCM.Application.Features.Loyalty;

public sealed class LoyaltyOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static LoyaltyOperationException Validation(string message) => new("validation_error", message);
    public static LoyaltyOperationException NotFound(string message) => new("not_found", message);
    public static LoyaltyOperationException Conflict(string message) => new("conflict", message);
    public static LoyaltyOperationException Forbidden(string message) => new("forbidden", message);
}
