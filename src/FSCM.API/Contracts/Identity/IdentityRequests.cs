namespace FSCM.API.Contracts.Identity;

/// <summary>
/// Local-login identifier.  A client may supply either <c>identifier</c>, or
/// the legacy <c>email</c>/<c>phone</c> fields while clients are migrated.
/// </summary>
/// <remarks>
/// <c>password</c> may be empty or omitted: a not-yet-activated Retailer signing in for the first
/// time with only the phone number receives <c>409 retailer_activation_required</c> and continues
/// with the OTP flow (<c>otp/request</c> → <c>otp/verify</c> → <c>password</c>).
/// </remarks>
public sealed record LoginRequest(string? Identifier, string? Password, string? Email = null, string? Phone = null)
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

/// <param name="Phone">Retailer phone number (10 digits starting with 0).</param>
/// <param name="Channel">Optional <c>Sms</c> or <c>Email</c>; defaults to <c>RetailerActivation:DefaultOtpChannel</c>.</param>
public sealed record RequestRetailerOtpRequest(string Phone, string? Channel = null);
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

