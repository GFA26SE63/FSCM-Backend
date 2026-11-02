using System.Net.Mail;
using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FSCM.Persistence.Seeding;

public sealed class AdminBootstrapper(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AdminBootstrapper> logger)
{
    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        if (await dbContext.UserAccountRecords.AnyAsync(
                user => user.Role == SystemRoles.Admin,
                cancellationToken))
        {
            logger.LogInformation("Admin bootstrap skipped because an Admin account already exists.");
            return;
        }

        var email = NormalizeEmail(Require("AdminBootstrap:Email"));
        var fullName = Require("AdminBootstrap:FullName").Trim();
        if (fullName.Length > 150)
            throw new InvalidOperationException("AdminBootstrap:FullName cannot exceed 150 characters.");
        var identityProvider = NormalizeProvider(Require("AdminBootstrap:IdentityProvider"));
        var googleSubject = Clean(configuration["AdminBootstrap:GoogleSubject"]);
        var password = configuration["AdminBootstrap:Password"];

        if (identityProvider == IdentityProviders.Google && string.IsNullOrWhiteSpace(googleSubject))
            throw new InvalidOperationException("AdminBootstrap:GoogleSubject is required for a Google Admin.");
        if (googleSubject?.Length > 255)
            throw new InvalidOperationException("AdminBootstrap:GoogleSubject cannot exceed 255 characters.");
        if (identityProvider == IdentityProviders.Local)
            ValidatePassword(password);
        if (await dbContext.UserAccountRecords.AnyAsync(
                user => user.Email == email ||
                    (googleSubject != null && user.GoogleSubject == googleSubject),
                cancellationToken))
            throw new InvalidOperationException("The configured Admin email or Google subject already exists.");

        var now = timeProvider.GetUtcNow();
        var admin = new UserAccount
        {
            Id = Guid.NewGuid(),
            GoogleSubject = googleSubject,
            IdentityProvider = identityProvider,
            Email = email,
            FullName = fullName,
            Role = SystemRoles.Admin,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.UserAccountRecords.Add(admin);
        if (identityProvider == IdentityProviders.Local)
        {
            dbContext.UserCredentials.Add(new UserCredential
            {
                Id = Guid.NewGuid(),
                UserAccountId = admin.Id,
                PasswordHash = passwordHasher.Hash(password!),
                MustChangePassword = true,
                PasswordChangedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        dbContext.AuditLogRecords.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserAccountId = admin.Id,
            EntityName = nameof(UserAccount),
            EntityId = admin.Id,
            Action = "AdminBootstrapped",
            NewValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                admin.Email,
                admin.FullName,
                admin.IdentityProvider
            }),
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "One-time Admin bootstrap created account {AdminEmail} using {IdentityProvider} authentication.",
            admin.Email,
            admin.IdentityProvider);
    }

    private string Require(string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"{key} must be supplied from environment or a secret store.");

    private static string NormalizeEmail(string value)
    {
        var email = value.Trim().ToLowerInvariant();
        if (email.Length > 150 || !MailAddress.TryCreate(email, out _))
            throw new InvalidOperationException("AdminBootstrap:Email must be a valid email address.");
        return email;
    }

    private static string NormalizeProvider(string value)
    {
        if (value.Equals(IdentityProviders.Local, StringComparison.OrdinalIgnoreCase))
            return IdentityProviders.Local;
        if (value.Equals(IdentityProviders.Google, StringComparison.OrdinalIgnoreCase))
            return IdentityProviders.Google;
        throw new InvalidOperationException("AdminBootstrap:IdentityProvider must be Local or Google.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < 12 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(character => !char.IsLetterOrDigit(character)))
            throw new InvalidOperationException(
                "AdminBootstrap:Password must be at least 12 characters and contain uppercase, lowercase, numeric, and special characters.");
    }
}
