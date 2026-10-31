namespace FSCM.API.Contracts.Identity;

/// <summary>
/// Local-login identifier.  A client may supply either <c>identifier</c>, or
/// the legacy <c>email</c>/<c>phone</c> fields while clients are migrated.
/// </summary>
public sealed record LoginRequest(string? Identifier, string Password, string? Email = null, string? Phone = null)
{
    public string ResolvedIdentifier =>
        !string.IsNullOrWhiteSpace(Identifier) ? Identifier :
        !string.IsNullOrWhiteSpace(Phone) ? Phone :
        Email ?? string.Empty;
}

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record ActivateAccountRequest(string Token, string Password);

public sealed record PasswordResetRequest(string Email);

public sealed record ConfirmPasswordResetRequest(string Token, string Password);

public sealed record RequestRetailerOtpRequest(string Phone);
public sealed record VerifyRetailerOtpRequest(string Phone, string Code);
public sealed record SetRetailerPasswordRequest(string SetupToken, string Password);

public sealed record CreateUserRequest(
    string Email,
    string FullName,
    string Role,
    string Phone,
    string Password);

public sealed record UpdateUserProfileRequest(string Email, string FullName);

public sealed record UpdateUserRoleRequest(string Role);

public sealed record UpdateUserStatusRequest(bool IsActive);

