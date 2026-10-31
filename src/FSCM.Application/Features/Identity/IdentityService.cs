using System.Net.Mail;
using System.Text.Json;
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
        string identifier,
        string password,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var normalizedIdentifier = identifier?.Trim() ?? string.Empty;
        var user = normalizedIdentifier.Contains('@', StringComparison.Ordinal)
            ? await store.FindUserByEmailAsync(NormalizeEmail(normalizedIdentifier), cancellationToken)
            : await store.FindUserByPhoneAsync(ValidatePhone(normalizedIdentifier), cancellationToken);
        var credential = user is null
            ? null
            : await store.FindCredentialAsync(user.Id, cancellationToken);

        // Authentication methods are capabilities, not mutually exclusive account types.
        // A password credential enables local login even after the same account is linked to Google.
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

    public async Task<AuthenticationResult> ExchangeGoogleAsync(
        GoogleIdentity identity,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identity.Subject) || identity.Subject.Trim().Length > 255)
            throw IdentityOperationException.InvalidToken();

        var normalizedEmail = NormalizeEmail(identity.Email);
        var subject = identity.Subject.Trim();
        var user = await store.FindUserByGoogleSubjectAsync(subject, cancellationToken);
        if (user is null)
        {
            user = await store.FindUserByEmailAsync(normalizedEmail, cancellationToken);
            if (user is null)
                throw IdentityOperationException.Forbidden(
                    "The Google identity has not been provisioned in FSCM.");
            if (!user.IsActive)
                throw IdentityOperationException.Forbidden("The FSCM account is inactive.");
            if (!string.IsNullOrWhiteSpace(user.GoogleSubject))
                throw IdentityOperationException.Conflict(
                    "The FSCM account is already linked to another Google identity.");

            user.GoogleSubject = subject;
            user.UpdatedAt = timeProvider.GetUtcNow();
            AddAudit(user.Id, user.Id, "GoogleIdentityLinked", null, new { GoogleSubject = subject });
        }

        if (!user.IsActive)
            throw IdentityOperationException.Forbidden("The FSCM account is inactive.");

        var now = timeProvider.GetUtcNow();
        var credential = await store.FindCredentialAsync(user.Id, cancellationToken);
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

        if (user is null || !user.IsActive ||
            (user.IdentityProvider == IdentityProviders.Local && credential is null))
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
        if (user.IdentityProvider != IdentityProviders.Local)
            throw IdentityOperationException.InvalidToken();
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
        if (user is null || !user.IsActive || user.IdentityProvider != IdentityProviders.Local)
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
        var scopeLabels = await store.GetScopeLabelsAsync(scope, cancellationToken);

        return ToAuthenticatedUser(user, credential?.MustChangePassword ?? false, scope, scopeLabels);
    }

    public async Task<PagedUserResult> ListUsersAsync(
        UserListQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Page < 1)
            throw IdentityOperationException.Validation("Page must be greater than zero.");
        if (query.PageSize is < 1 or > 100)
            throw IdentityOperationException.Validation("Page size must be between 1 and 100.");

        var normalizedRole = string.IsNullOrWhiteSpace(query.Role) ? null : query.Role.Trim();
        if (normalizedRole is not null && !SystemRoles.IsValid(normalizedRole))
            throw IdentityOperationException.Validation("Role filter is invalid.");
        var normalizedQuery = query with { Search = query.Search?.Trim(), Role = normalizedRole };
        var result = await store.ListUsersAsync(normalizedQuery, cancellationToken);
        return new PagedUserResult(
            result.Items.Select(ToUserSummary).ToArray(),
            query.Page,
            query.PageSize,
            result.TotalCount);
    }

    public async Task<AuthenticatedUser> UpdateProfileAsync(
        Guid userId,
        string email,
        string fullName,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await GetManageableUserAsync(userId, cancellationToken);
        var normalizedEmail = NormalizeEmail(email);
        ValidateFullName(fullName);
        var duplicate = await store.FindUserByEmailAsync(normalizedEmail, cancellationToken);
        if (duplicate is not null && duplicate.Id != userId)
            throw IdentityOperationException.Conflict("A user with this email address already exists.");

        var before = new { user.Email, user.FullName };
        user.Email = normalizedEmail;
        user.FullName = fullName.Trim();
        user.UpdatedAt = timeProvider.GetUtcNow();
        AddAudit(user.Id, actorUserId, "ProfileUpdated", before, new { user.Email, user.FullName });
        await store.SaveChangesAsync(cancellationToken);
        return await GetCurrentUserAsync(userId, cancellationToken);
    }

    public async Task<CreatedUserResult> CreateUserAsync(
        string email,
        string fullName,
        string role,
        string phone,
        string password,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        ValidateFullName(fullName);
        phone = ValidatePhone(phone);
        ValidatePassword(password);
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
            IdentityProvider = IdentityProviders.Local,
            Email = normalizedEmail,
            Phone = phone,
            FullName = fullName.Trim(),
            Role = role,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        store.AddUser(user);
        store.AddCredential(new UserCredential
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            PasswordHash = passwordHasher.Hash(password),
            FailedLoginCount = 0,
            MustChangePassword = false,
            PasswordChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        AddAudit(user.Id, actorUserId, "Created", null,
            new { user.Email, user.Phone, user.FullName, user.Role, user.IdentityProvider, user.IsActive });
        await store.SaveChangesAsync(cancellationToken);

        return new CreatedUserResult(
            user.Id,
            user.Email,
            user.Role,
            user.IdentityProvider,
            null,
            null);
    }

    public async Task UpdateRoleAsync(
        Guid userId,
        string role,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureAssignableRole(role);
        var user = await GetManageableUserAsync(userId, cancellationToken);
        var oldRole = user.Role;
        user.Role = role;
        user.UpdatedAt = timeProvider.GetUtcNow();
        AddAudit(user.Id, actorUserId, "RoleUpdated", new { Role = oldRole }, new { user.Role });
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveStatusAsync(
        Guid userId,
        bool isActive,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await GetManageableUserAsync(userId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var oldIsActive = user.IsActive;
        user.IsActive = isActive;
        user.UpdatedAt = now;
        if (!isActive)
        {
            await RevokeActiveRefreshTokensAsync(userId, now, cancellationToken);
        }

        AddAudit(user.Id, actorUserId, isActive ? "Activated" : "Deactivated",
            new { IsActive = oldIsActive }, new { user.IsActive });
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignWarehouseAsync(
        Guid userId,
        Guid warehouseId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await GetManageableUserAsync(userId, cancellationToken);
        if (user.Role != SystemRoles.WarehouseKeeper)
            throw IdentityOperationException.Validation(
                "Only Warehouse Keeper users can be assigned to a warehouse.");
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

        AddAudit(userId, actorUserId, "WarehouseAssigned", null, new { WarehouseId = warehouseId });
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignWarehouseAsync(
        Guid userId,
        Guid warehouseId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await GetManageableUserAsync(userId, cancellationToken);
        var assignment = await store.FindWarehouseAssignmentAsync(userId, warehouseId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Warehouse assignment was not found.");
        assignment.IsActive = false;
        AddAudit(userId, actorUserId, "WarehouseUnassigned", new { WarehouseId = warehouseId }, null);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignSalesGroupAsync(
        Guid userId,
        Guid salesGroupId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await GetManageableUserAsync(userId, cancellationToken);
        if (user.Role is not (SystemRoles.Sales or SystemRoles.SalesManager))
        {
            throw IdentityOperationException.Validation(
                "Only Sales and Sales Manager users can be assigned to a Sales group.");
        }
        if (!await store.SalesGroupExistsAsync(salesGroupId, cancellationToken))
        {
            throw IdentityOperationException.NotFound("Sales group was not found.");
        }

        var now = timeProvider.GetUtcNow();
        var activeMemberships = await store.ListActiveSalesGroupMembershipsAsync(userId, cancellationToken);
        if (activeMemberships.Any(membership => membership.SalesGroupId == salesGroupId))
        {
            return;
        }

        foreach (var activeMembership in activeMemberships)
        {
            activeMembership.IsActive = false;
            activeMembership.UnassignedAt = now;
        }

        store.AddSalesGroupMembership(new SalesGroupMember
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            SalesGroupId = salesGroupId,
            AssignedAt = now,
            IsActive = true
        });

        AddAudit(userId, actorUserId, "SalesGroupAssigned", null, new { SalesGroupId = salesGroupId });
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignSalesGroupAsync(
        Guid userId,
        Guid salesGroupId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await GetManageableUserAsync(userId, cancellationToken);
        var membership = await store.FindSalesGroupMembershipAsync(userId, salesGroupId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Sales group membership was not found.");
        membership.IsActive = false;
        membership.UnassignedAt = timeProvider.GetUtcNow();
        AddAudit(userId, actorUserId, "SalesGroupUnassigned", new { SalesGroupId = salesGroupId }, null);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignRegionAsync(
        Guid userId,
        Guid regionId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await GetManageableUserAsync(userId, cancellationToken);
        if (user.Role != SystemRoles.SalesManager)
            throw IdentityOperationException.Validation("Only Sales Manager users can be assigned directly to a region.");
        if (!await store.RegionExistsAsync(regionId, cancellationToken))
            throw IdentityOperationException.NotFound("Region was not found.");

        var now = timeProvider.GetUtcNow();
        var assignment = await store.FindRegionAssignmentAsync(userId, regionId, cancellationToken);
        if (assignment is null)
        {
            store.AddRegionAssignment(new UserRegionAssignment
            {
                Id = Guid.NewGuid(),
                UserAccountId = userId,
                RegionId = regionId,
                AssignedAt = now,
                IsActive = true
            });
        }
        else
        {
            assignment.AssignedAt = now;
            assignment.UnassignedAt = null;
            assignment.IsActive = true;
        }
        AddAudit(userId, actorUserId, "RegionAssigned", null, new { RegionId = regionId });
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignRegionAsync(
        Guid userId,
        Guid regionId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await GetManageableUserAsync(userId, cancellationToken);
        var assignment = await store.FindRegionAssignmentAsync(userId, regionId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Region assignment was not found.");
        assignment.IsActive = false;
        assignment.UnassignedAt = timeProvider.GetUtcNow();
        AddAudit(userId, actorUserId, "RegionUnassigned", new { RegionId = regionId }, null);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignRetailerAsync(
        Guid userId,
        Guid retailerId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var user = await GetManageableUserAsync(userId, cancellationToken);
        if (user.Role != SystemRoles.Retailer)
            throw IdentityOperationException.Validation("Only Retailer users can be assigned to a Retailer profile.");
        var retailer = await store.FindRetailerAsync(retailerId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Retailer was not found.");
        if (retailer.UserAccountId.HasValue && retailer.UserAccountId != userId)
            throw IdentityOperationException.Conflict("The Retailer is already assigned to another user.");
        var current = await store.FindRetailerByUserIdAsync(userId, cancellationToken);
        if (current is not null && current.Id != retailerId)
            throw IdentityOperationException.Conflict("The user is already assigned to another Retailer.");

        retailer.UserAccountId = userId;
        retailer.UpdatedAt = timeProvider.GetUtcNow();
        AddAudit(userId, actorUserId, "RetailerAssigned", null, new { RetailerId = retailerId });
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task UnassignRetailerAsync(
        Guid userId,
        Guid retailerId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await GetManageableUserAsync(userId, cancellationToken);
        var retailer = await store.FindRetailerAsync(retailerId, cancellationToken)
            ?? throw IdentityOperationException.NotFound("Retailer was not found.");
        if (retailer.UserAccountId != userId)
            throw IdentityOperationException.NotFound("Retailer assignment was not found.");
        retailer.UserAccountId = null;
        retailer.UpdatedAt = timeProvider.GetUtcNow();
        AddAudit(userId, actorUserId, "RetailerUnassigned", new { RetailerId = retailerId }, null);
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthenticationResult> IssueAuthenticationResultAsync(
        UserAccount user,
        UserCredential? credential,
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
            ToAuthenticatedUser(user, credential?.MustChangePassword ?? false, scope));
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
        IdentityScope scope,
        IdentityScopeLabels? scopeLabels = null) =>
        new(user.Id, user.Email, user.FullName, user.Role, mustChangePassword, scope,
            scopeLabels ?? IdentityScopeLabels.Empty);

    private static UserSummary ToUserSummary(UserAccount user) =>
        new(user.Id, user.Email, user.FullName, user.Role, user.IsActive,
            user.IdentityProvider, user.CreatedAt, user.UpdatedAt);

    private void AddAudit(Guid entityId, Guid actorUserId, string action, object? oldValues, object? newValues) =>
        store.AddAuditLog(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = actorUserId,
            EntityName = nameof(UserAccount),
            EntityId = entityId,
            Action = action,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
            CreatedAt = timeProvider.GetUtcNow()
        });

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

    internal static void ValidatePasswordForActivation(string password) => ValidatePassword(password);

    private static string ValidatePhone(string phone)
    {
        var normalized = phone?.Trim() ?? string.Empty;
        if (normalized.Length != 10 || normalized[0] != '0' || normalized.Any(character => !char.IsDigit(character)))
        {
            throw IdentityOperationException.Validation(
                "Phone number must contain 10 digits and start with 0.");
        }

        return normalized;
    }

    private static void EnsureAssignableRole(string role)
    {
        if (!SystemRoles.CanBeAssignedThroughApi(role))
        {
            throw IdentityOperationException.Forbidden(
                "The requested role is invalid or cannot be assigned through this API.");
        }
    }

    private static string NormalizeIdentityProvider(string identityProvider)
    {
        if (string.IsNullOrWhiteSpace(identityProvider))
            throw IdentityOperationException.Validation("Identity provider must be Local or Google.");
        if (identityProvider.Equals(IdentityProviders.Local, StringComparison.OrdinalIgnoreCase))
            return IdentityProviders.Local;
        if (identityProvider.Equals(IdentityProviders.Google, StringComparison.OrdinalIgnoreCase))
            return IdentityProviders.Google;
        throw IdentityOperationException.Validation("Identity provider must be Local or Google.");
    }

    private static string? LimitIpAddress(string? ipAddress) =>
        string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress[..Math.Min(ipAddress.Length, 64)];
}
