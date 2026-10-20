namespace FSCM.API.Contracts.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record ActivateAccountRequest(string Token, string Password);

public sealed record PasswordResetRequest(string Email);

public sealed record ConfirmPasswordResetRequest(string Token, string Password);

public sealed record CreateUserRequest(
    string Email,
    string FullName,
    string Role,
    string IdentityProvider = "Local");

public sealed record UpdateUserProfileRequest(string Email, string FullName);

public sealed record UpdateUserRoleRequest(string Role);

public sealed record UpdateUserStatusRequest(bool IsActive);

