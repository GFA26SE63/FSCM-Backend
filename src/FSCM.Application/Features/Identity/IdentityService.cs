using System.Net.Mail;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Identity;

public sealed class IdentityService(
    IIdentityStore store,
    IPasswordHasher passwordHasher,
    IIdentityTokenProvider tokenProvider,
    TimeProvider timeProvider)
{
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ActivationTokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<AuthenticationResult> LoginAsync(
        string email,
        string password,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await store.FindUserByEmailAsync(normalizedEmail, cancellationToken);
        var credential = user is null
            ? null
            : await store.FindCredentialAsync(user.Id, cancellationToken);

        if (user is null || credential is null || !user.IsActive)
        {
            throw IdentityOperationException.InvalidCredentials();
        }

        var now = timeProvider.GetUtcNow();
        if (credential.LockoutEndAt > now)
        {
            throw IdentityOperationException.AccountLocked();
        }

        if (!passwordHasher.Verify(password, credential.PasswordHash))
        {
            credential.FailedLoginCount++;
            credential.UpdatedAt = now;
            if (credential.FailedLoginCount >= LockoutThreshold)
            {
                credential.FailedLoginCount = 0;
                credential.LockoutEndAt = now.Add(LockoutDuration);
            }

            await store.SaveChangesAsync(cancellationToken);
            throw IdentityOperationException.InvalidCredentials();
        }

        credential.FailedLoginCount = 0;
        credential.LockoutEndAt = null;
        credential.UpdatedAt = now;

        return await IssueAuthenticationResultAsync(user, credential, ipAddress, now, cancellationToken);
    }

    public async Task<AuthenticationResult> RefreshAsync(
        string rawRefreshToken,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tokenHash = tokenProvider.HashOpaqueToken(rawRefreshToken);
        var currentToken = await store.FindRefreshTokenAsync(tokenHash, cancellationToken);

        if (currentToken is null || currentToken.ExpiresAt <= now || currentToken.RevokedAt is not null)
        {
            throw IdentityOperationException.InvalidToken();
        }

        var user = await store.FindUserByIdAsync(currentToken.UserAccountId, cancellationToken);
        var credential = user is null
            ? null
            : await store.FindCredentialAsync(user.Id, cancellationToken);

        if (user is null || credential is null || !user.IsActive)
        {
            throw IdentityOperationException.InvalidToken();
        }

        currentToken.RevokedAt = now;
        currentToken.RevokedByIpAddress = LimitIpAddress(ipAddress);

        var result = await IssueAuthenticationResultAsync(user, credential, ipAddress, now, cancellationToken);
        var replacement = await store.FindRefreshTokenAsync(
            tokenProvider.HashOpaqueToken(result.RefreshToken),
            cancellationToken);
        currentToken.ReplacedByTokenId = replacement!.Id;
        await store.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task LogoutAsync(
        string rawRefreshToken,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenProvider.HashOpaqueToken(rawRefreshToken);
        var token = await store.FindRefreshTokenAsync(tokenHash, cancellationToken);
        if (token is null || token.RevokedAt is not null)
        {
            return;
        }

        token.RevokedAt = timeProvider.GetUtcNow();
        token.RevokedByIpAddress = LimitIpAddress(ipAddress);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task ActivateAsync(
        string rawToken,
        string password,
        CancellationToken cancellationToken)
    {
        ValidatePassword(password);
        var now = timeProvider.GetUtcNow();
        var token = await FindUsableAccountTokenAsync(
            rawToken,
            AccountTokenPurposes.Activation,
            now,
            cancellationToken);
        var user = await store.FindUserByIdAsync(token.UserAccountId, cancellationToken)
            ?? throw IdentityOperationException.InvalidToken();
        var credential = await store.FindCredentialAsync(user.Id, cancellationToken);

        if (credential is null)
        {
            credential = new UserCredential
            {
                Id = Guid.NewGuid(),
                UserAccountId = user.Id,
                CreatedAt = now
            };
            store.AddCredential(credential);
        }

        credential.PasswordHash = passwordHasher.Hash(password);
        credential.FailedLoginCount = 0;
        credential.LockoutEndAt = null;
        credential.MustChangePassword = false;
        credential.PasswordChangedAt = now;
        credential.UpdatedAt = now;
        user.IsActive = true;
        user.UpdatedAt = now;
        token.ConsumedAt = now;

        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var user = await store.FindUserByEmailAsync(NormalizeEmail(email), cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var (rawToken, accountToken) = CreateAccountToken(
            user.Id,
            AccountTokenPurposes.PasswordReset,
            now,
            PasswordResetTokenLifetime);
        store.AddAccountToken(accountToken);
        await store.SaveChangesAsync(cancellationToken);

        return rawToken;
    }

    public async Task ResetPasswordAsync(
        string rawToken,
        string password,
        CancellationToken cancellationToken)
    {
        ValidatePassword(password);
        var now = timeProvider.GetUtcNow();
        var token = await FindUsableAccountTokenAsync(
            rawToken,
            AccountTokenPurposes.PasswordReset,
            now,
            cancellationToken);
        var credential = await store.FindCredentialAsync(token.UserAccountId, cancellationToken)
            ?? throw IdentityOperationException.InvalidToken();

        credential.PasswordHash = passwordHasher.Hash(password);
        credential.FailedLoginCount = 0;
        credential.LockoutEndAt = null;
        credential.MustChangePassword = false;
        credential.PasswordChangedAt = now;
        credential.UpdatedAt = now;
        token.ConsumedAt = now;
        await RevokeActiveRefreshTokensAsync(token.UserAccountId, now, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthenticatedUser> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("User account was not found.");
        var credential = await store.FindCredentialAsync(userId, cancellationToken);
        var scope = await store.GetScopeAsync(userId, cancellationToken);

        return ToAuthenticatedUser(user, credential?.MustChangePassword ?? false, scope);
    }

    public async Task<CreatedUserResult> CreateUserAsync(
        string email,
        string fullName,
        string role,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        ValidateFullName(fullName);
        EnsureAssignableRole(role);

        if (await store.FindUserByEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            throw IdentityOperationException.Conflict("A user with this email address already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var userId = Guid.NewGuid();
        var user = new UserAccount
        {
            Id = userId,
            EntraObjectId = $"local:{userId:N}",
            Email = normalizedEmail,
            FullName = fullName.Trim(),
            Role = role,
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        var (rawToken, accountToken) = CreateAccountToken(
            userId,
            AccountTokenPurposes.Activation,
            now,
            ActivationTokenLifetime);

        store.AddUser(user);
        store.AddAccountToken(accountToken);
        await store.SaveChangesAsync(cancellationToken);

        return new CreatedUserResult(user.Id, user.Email, user.Role, rawToken, accountToken.ExpiresAt);
    }

    public async Task UpdateRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken)
    {
        EnsureAssignableRole(role);
        var user = await GetManageableUserAsync(userId, cancellationToken);
        user.Role = role;
        user.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveStatusAsync(
        Guid userId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var user = await GetManageableUserAsync(userId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        user.IsActive = isActive;
        user.UpdatedAt = now;
        if (!isActive)
        {
            await RevokeActiveRefreshTokensAsync(userId, now, cancellationToken);
        }

        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignWarehouseAsync(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        await GetManageableUserAsync(userId, cancellationToken);
        if (!await store.WarehouseExistsAsync(warehouseId, cancellationToken))
        {
            throw IdentityOperationException.NotFound("Warehouse was not found.");
        }

        var assignment = await store.FindWarehouseAssignmentAsync(userId, warehouseId, cancellationToken);
        if (assignment is null)
        {
            store.AddWarehouseAssignment(new UserWarehouseAssignment
            {
                Id = Guid.NewGuid(),
                UserAccountId = userId,
                WarehouseId = warehouseId,
                AssignedAt = timeProvider.GetUtcNow(),
                IsActive = true
            });
        }
        else
        {
            assignment.IsActive = true;
            assignment.AssignedAt = timeProvider.GetUtcNow();
        }

        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignWarehouseAsync(
        Guid userId,
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var assignment = await store.FindWarehouseAssignmentAsync(userId, warehouseId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Warehouse assignment was not found.");
        assignment.IsActive = false;
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignSalesGroupAsync(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken)
    {
        await GetManageableUserAsync(userId, cancellationToken);
        if (!await store.SalesGroupExistsAsync(salesGroupId, cancellationToken))
        {
            throw IdentityOperationException.NotFound("Sales group was not found.");
        }

        var now = timeProvider.GetUtcNow();
        var membership = await store.FindSalesGroupMembershipAsync(userId, salesGroupId, cancellationToken);
        if (membership is null)
        {
            store.AddSalesGroupMembership(new SalesGroupMember
            {
                Id = Guid.NewGuid(),
                UserAccountId = userId,
                SalesGroupId = salesGroupId,
                AssignedAt = now,
                IsActive = true
            });
        }
        else
        {
            membership.IsActive = true;
            membership.AssignedAt = now;
            membership.UnassignedAt = null;
        }

        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignSalesGroupAsync(
        Guid userId,
        Guid salesGroupId,
        CancellationToken cancellationToken)
    {
        var membership = await store.FindSalesGroupMembershipAsync(userId, salesGroupId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Sales group membership was not found.");
        membership.IsActive = false;
        membership.UnassignedAt = timeProvider.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthenticationResult> IssueAuthenticationResultAsync(
        UserAccount user,
        UserCredential credential,
        string? ipAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scope = await store.GetScopeAsync(user.Id, cancellationToken);
        var accessToken = tokenProvider.CreateAccessToken(user, scope, now);
        var rawRefreshToken = tokenProvider.GenerateOpaqueToken();
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = user.Id,
            TokenHash = tokenProvider.HashOpaqueToken(rawRefreshToken),
            CreatedAt = now,
            ExpiresAt = now.Add(tokenProvider.RefreshTokenLifetime),
            CreatedByIpAddress = LimitIpAddress(ipAddress)
        };

        store.AddRefreshToken(refreshToken);
        await store.SaveChangesAsync(cancellationToken);

        return new AuthenticationResult(
            accessToken.Token,
            accessToken.ExpiresAt,
            rawRefreshToken,
            refreshToken.ExpiresAt,
            ToAuthenticatedUser(user, credential.MustChangePassword, scope));
    }

    private async Task<AccountToken> FindUsableAccountTokenAsync(
        string rawToken,
        string purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenProvider.HashOpaqueToken(rawToken);
        var token = await store.FindAccountTokenAsync(tokenHash, purpose, cancellationToken);
        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= now)
        {
            throw IdentityOperationException.InvalidToken();
        }

        return token;
    }

    private (string RawToken, AccountToken AccountToken) CreateAccountToken(
        Guid userId,
        string purpose,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        var rawToken = tokenProvider.GenerateOpaqueToken();
        return (rawToken, new AccountToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            Purpose = purpose,
            TokenHash = tokenProvider.HashOpaqueToken(rawToken),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime)
        });
    }

    private async Task<UserAccount> GetManageableUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("User account was not found.");
        if (user.Role == SystemRoles.Admin)
        {
            throw IdentityOperationException.Forbidden("Admin accounts cannot be modified through this API.");
        }

        return user;
    }

    private async Task RevokeActiveRefreshTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tokens = await store.GetActiveRefreshTokensAsync(userId, now, cancellationToken);
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
    }

    private static AuthenticatedUser ToAuthenticatedUser(
        UserAccount user,
        bool mustChangePassword,
        IdentityScope scope) =>
        new(user.Id, user.Email, user.FullName, user.Role, mustChangePassword, scope);

    private static string NormalizeEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 150 || !MailAddress.TryCreate(normalized, out _))
        {
            throw IdentityOperationException.Validation("A valid email address is required.");
        }

        return normalized;
    }

    private static void ValidateFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 150)
        {
            throw IdentityOperationException.Validation("Full name is required and must not exceed 150 characters.");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw IdentityOperationException.Validation(
                "Password must be at least 12 characters and contain uppercase, lowercase, numeric, and special characters.");
        }
    }

    private static void EnsureAssignableRole(string role)
    {
        if (!SystemRoles.CanBeAssignedThroughApi(role))
        {
            throw IdentityOperationException.Forbidden(
                "The requested role is invalid or cannot be assigned through this API.");
        }
    }

    private static string? LimitIpAddress(string? ipAddress) =>
        string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress[..Math.Min(ipAddress.Length, 64)];
}
