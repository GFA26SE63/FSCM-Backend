using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FSCM.Persistence.Seeding;

public sealed class DevelopmentDataSeeder(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<DevelopmentDataSeeder> logger)
{
    private const string DemoWarehouseCode = "WH-HCM-01";
    private const string DemoRegionCode = "REG-HCM";
    private const string DemoSalesGroupCode = "SG-HCM-01";
    private const string DemoRetailerCode = "RTL-HCM-001";

    private static readonly (string Email, string FullName, string Role)[] DemoUsers =
    [
        ("admin@fscm.local", "FSCM Administrator", SystemRoles.Admin),
        ("operator@fscm.local", "FSCM Operator", SystemRoles.Operator),
        ("sales.manager@fscm.local", "FSCM Sales Manager", SystemRoles.SalesManager),
        ("sales@fscm.local", "FSCM Sales Representative", SystemRoles.Sales),
        ("warehouse.keeper@fscm.local", "FSCM Warehouse Keeper", SystemRoles.WarehouseKeeper),
        ("retailer@fscm.local", "FSCM Retailer User", SystemRoles.Retailer)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var password = configuration["SeedData:DefaultPassword"];
        ValidatePassword(password);
        await dbContext.Database.MigrateAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var users = await SeedUsersAsync(password!, now, cancellationToken);
        var region = await SeedRegionAsync(now, cancellationToken);
        var warehouse = await SeedWarehouseAsync(now, cancellationToken);
        var salesGroup = await SeedSalesGroupAsync(region.Id, now, cancellationToken);
        var retailer = await SeedRetailerAsync(region.Id, users[SystemRoles.Sales].Id, now, cancellationToken);

        await SeedWarehouseAssignmentsAsync(users, warehouse.Id, now, cancellationToken);
        await SeedSalesGroupMembershipsAsync(users, salesGroup.Id, now, cancellationToken);
        await SeedRetailerAssignmentAsync(retailer.Id, salesGroup.Id, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Development data seeded: {UserCount} users, warehouse {WarehouseCode}, sales group {SalesGroupCode}, retailer {RetailerCode}.",
            users.Count,
            DemoWarehouseCode,
            DemoSalesGroupCode,
            DemoRetailerCode);
    }

    private async Task<Dictionary<string, UserAccount>> SeedUsersAsync(
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var usersByRole = new Dictionary<string, UserAccount>(StringComparer.Ordinal);
        foreach (var definition in DemoUsers)
        {
            var user = await dbContext.UserAccountRecords.SingleOrDefaultAsync(
                candidate => candidate.Email == definition.Email,
                cancellationToken);
            if (user is null)
            {
                var userId = Guid.NewGuid();
                user = new UserAccount
                {
                    Id = userId,
                    EntraObjectId = $"local:{userId:N}",
                    Email = definition.Email,
                    CreatedAt = now
                };
                dbContext.UserAccountRecords.Add(user);
            }

            user.FullName = definition.FullName;
            user.Role = definition.Role;
            user.IsActive = true;
            user.UpdatedAt = now;
            usersByRole.Add(definition.Role, user);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var user in usersByRole.Values)
        {
            var credential = await dbContext.UserCredentials.SingleOrDefaultAsync(
                candidate => candidate.UserAccountId == user.Id,
                cancellationToken);
            if (credential is null)
            {
                credential = new UserCredential
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = user.Id,
                    CreatedAt = now
                };
                dbContext.UserCredentials.Add(credential);
            }

            credential.PasswordHash = passwordHasher.Hash(password);
            credential.FailedLoginCount = 0;
            credential.LockoutEndAt = null;
            credential.MustChangePassword = false;
            credential.PasswordChangedAt = now;
            credential.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return usersByRole;
    }

    private async Task<Region> SeedRegionAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var region = await dbContext.RegionRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == DemoRegionCode,
            cancellationToken);
        if (region is not null)
        {
            return region;
        }

        region = new Region
        {
            Id = Guid.NewGuid(),
            Code = DemoRegionCode,
            Name = "Ho Chi Minh City",
            Description = "Development seed region",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.RegionRecords.Add(region);
        await dbContext.SaveChangesAsync(cancellationToken);
        return region;
    }

    private async Task<Warehouse> SeedWarehouseAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var warehouse = await dbContext.WarehouseRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == DemoWarehouseCode,
            cancellationToken);
        if (warehouse is not null)
        {
            return warehouse;
        }

        warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            Code = DemoWarehouseCode,
            Name = "Ho Chi Minh Central Warehouse",
            Address = "Thu Duc City, Ho Chi Minh City",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.WarehouseRecords.Add(warehouse);
        await dbContext.SaveChangesAsync(cancellationToken);
        return warehouse;
    }

    private async Task<SalesGroup> SeedSalesGroupAsync(
        Guid regionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var salesGroup = await dbContext.SalesGroupRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == DemoSalesGroupCode,
            cancellationToken);
        if (salesGroup is not null)
        {
            return salesGroup;
        }

        salesGroup = new SalesGroup
        {
            Id = Guid.NewGuid(),
            RegionId = regionId,
            Code = DemoSalesGroupCode,
            Name = "Ho Chi Minh Sales Group 01",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.SalesGroupRecords.Add(salesGroup);
        await dbContext.SaveChangesAsync(cancellationToken);
        return salesGroup;
    }

    private async Task<Retailer> SeedRetailerAsync(
        Guid regionId,
        Guid declaredByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var retailer = await dbContext.RetailerRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == DemoRetailerCode,
            cancellationToken);
        if (retailer is not null)
        {
            return retailer;
        }

        retailer = new Retailer
        {
            Id = Guid.NewGuid(),
            RegionId = regionId,
            Code = DemoRetailerCode,
            Name = "FSCM Demo Retailer",
            ContactName = "Demo Store Manager",
            Phone = "+84 28 0000 0001",
            Email = "store@fscm.local",
            Address = "District 1, Ho Chi Minh City",
            Status = "Active",
            DeclaredByUserId = declaredByUserId,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.RetailerRecords.Add(retailer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return retailer;
    }

    private async Task SeedWarehouseAssignmentsAsync(
        IReadOnlyDictionary<string, UserAccount> users,
        Guid warehouseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var role in new[] { SystemRoles.Operator, SystemRoles.WarehouseKeeper })
        {
            var userId = users[role].Id;
            var assignment = await dbContext.UserWarehouseAssignmentRecords.SingleOrDefaultAsync(
                candidate => candidate.UserAccountId == userId && candidate.WarehouseId == warehouseId,
                cancellationToken);
            if (assignment is null)
            {
                assignment = new UserWarehouseAssignment
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = userId,
                    WarehouseId = warehouseId,
                    AssignedAt = now
                };
                dbContext.UserWarehouseAssignmentRecords.Add(assignment);
            }

            assignment.IsActive = true;
        }
    }

    private async Task SeedSalesGroupMembershipsAsync(
        IReadOnlyDictionary<string, UserAccount> users,
        Guid salesGroupId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var role in new[] { SystemRoles.SalesManager, SystemRoles.Sales, SystemRoles.Retailer })
        {
            var userId = users[role].Id;
            var membership = await dbContext.SalesGroupMemberRecords.SingleOrDefaultAsync(
                candidate => candidate.UserAccountId == userId && candidate.SalesGroupId == salesGroupId,
                cancellationToken);
            if (membership is null)
            {
                membership = new SalesGroupMember
                {
                    Id = Guid.NewGuid(),
                    UserAccountId = userId,
                    SalesGroupId = salesGroupId,
                    AssignedAt = now
                };
                dbContext.SalesGroupMemberRecords.Add(membership);
            }

            membership.IsActive = true;
            membership.UnassignedAt = null;
        }
    }

    private async Task SeedRetailerAssignmentAsync(
        Guid retailerId,
        Guid salesGroupId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.RetailerSalesAssignmentRecords.SingleOrDefaultAsync(
            candidate => candidate.RetailerId == retailerId && candidate.SalesGroupId == salesGroupId,
            cancellationToken);
        if (assignment is null)
        {
            assignment = new RetailerSalesAssignment
            {
                Id = Guid.NewGuid(),
                RetailerId = retailerId,
                SalesGroupId = salesGroupId,
                AssignedAt = now
            };
            dbContext.RetailerSalesAssignmentRecords.Add(assignment);
        }

        assignment.IsActive = true;
        assignment.UnassignedAt = null;
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < 12 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new InvalidOperationException(
                "SeedData:DefaultPassword must be at least 12 characters and contain uppercase, lowercase, numeric, and special characters.");
        }
    }
}

