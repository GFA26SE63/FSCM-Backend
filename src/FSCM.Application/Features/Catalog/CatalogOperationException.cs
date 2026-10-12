namespace FSCM.Application.Features.Catalog;

public sealed class CatalogOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static CatalogOperationException Validation(string message) =>
        new("validation_error", message);

    public static CatalogOperationException NotFound(string message) =>
        new("not_found", message);

    public static CatalogOperationException Conflict(string message) =>
        new("conflict", message);

    public static CatalogOperationException InvalidTransition(string message) =>
        new("invalid_transition", message);
}
