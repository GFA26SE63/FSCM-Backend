namespace FSCM.Domain.Entities;

public sealed class RetailerActivationChallenge
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string OtpHash { get; set; } = string.Empty;
    public string? SetupTokenHash { get; set; }
    public string? RequestedIpAddress { get; set; }
    public int FailedAttemptCount { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? SetupTokenExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
