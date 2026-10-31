using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Identity;

public sealed record OtpRequestResult(string MaskedEmail, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);
public sealed record OtpVerificationResult(string SetupToken, DateTimeOffset ExpiresAt);

public interface ITransactionalEmailSender
{
    Task SendRetailerActivationOtpAsync(string recipientEmail, string recipientName, string otp, CancellationToken cancellationToken);
}

public interface IRetailerActivationStore
{
    Task<UserAccount?> FindUserByPhoneAsync(string phone, CancellationToken cancellationToken);
    Task<UserAccount?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<Retailer?> FindRetailerByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<RetailerActivationChallenge?> FindLatestAsync(Guid userId, CancellationToken cancellationToken);
    Task<RetailerActivationChallenge?> FindBySetupTokenHashAsync(string hash, CancellationToken cancellationToken);
    Task<int> CountRequestsForAccountAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken);
    Task<int> CountRequestsForIpAsync(string ipAddress, DateTimeOffset since, CancellationToken cancellationToken);
    void AddChallenge(RetailerActivationChallenge challenge);
    void AddCredential(UserCredential credential);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
