namespace FSCM.Domain.Entities;

public sealed class UserCredential
{
    public Guid Id { get; set; }

    public Guid UserAccountId { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockoutEndAt { get; set; }

    public bool MustChangePassword { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
