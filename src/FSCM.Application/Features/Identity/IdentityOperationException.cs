namespace FSCM.Application.Features.Identity;

public sealed class IdentityOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static IdentityOperationException InvalidCredentials() =>
        new("invalid_credentials", "The email address or password is invalid.");

    public static IdentityOperationException AccountLocked() =>
        new("account_locked", "The account is temporarily locked. Try again later.");

    public static IdentityOperationException InvalidToken() =>
        new("invalid_token", "The token is invalid or has expired.");

    public static IdentityOperationException Conflict(string message) =>
        new("conflict", message);

    public static IdentityOperationException Forbidden(string message) =>
        new("forbidden", message);

    public static IdentityOperationException NotFound(string message) =>
        new("not_found", message);

    public static IdentityOperationException Validation(string message) =>
        new("validation_error", message);
}

