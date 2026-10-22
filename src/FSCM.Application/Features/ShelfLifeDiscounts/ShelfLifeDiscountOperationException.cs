namespace FSCM.Application.Features.ShelfLifeDiscounts;

public sealed class ShelfLifeDiscountOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static ShelfLifeDiscountOperationException Validation(string message) => new("validation_error", message);
    public static ShelfLifeDiscountOperationException NotFound(string message) => new("not_found", message);
    public static ShelfLifeDiscountOperationException Conflict(string message) => new("conflict", message);
}
