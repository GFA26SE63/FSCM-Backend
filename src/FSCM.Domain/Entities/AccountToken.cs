namespace FSCM.Domain.Entities;

public sealed class AccountToken
{
    public Guid Id { get; set; }

    public Guid UserAccountId { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
