namespace FSCM.Application.Features.Identity;

public sealed class IdentityOperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static IdentityOperationException InvalidCredentials() =>
        new("invalid_credentials", "The email address, phone number, or password is invalid.");

    public static IdentityOperationException RetailerActivationRequired() =>
        new("retailer_activation_required", "Retailer cần kích hoạt tài khoản bằng OTP trước khi đăng nhập lần đầu.");

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

    public static IdentityOperationException RateLimited(string message) =>
        new("rate_limited", message);

    public static IdentityOperationException EmailDeliveryFailed() =>
        new("email_delivery_failed", "Không thể gửi email OTP. Vui lòng thử lại sau.");

    public static IdentityOperationException OtpExpired() =>
        new("otp_expired", "OTP đã hết hạn. Vui lòng yêu cầu mã mới.");

    public static IdentityOperationException OtpAttemptsExceeded() =>
        new("otp_attempts_exceeded", "OTP đã bị khóa sau 5 lần nhập sai. Vui lòng yêu cầu mã mới.");

    public static IdentityOperationException InvalidOtp(int? remainingAttempts = null) =>
        new("invalid_otp", remainingAttempts.HasValue
            ? $"OTP không đúng. Còn {remainingAttempts.Value} lần thử."
            : "OTP không đúng.");
}

