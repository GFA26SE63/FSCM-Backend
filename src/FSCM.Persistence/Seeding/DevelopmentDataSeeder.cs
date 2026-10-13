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
        ("admin@fscm.local", "Quản trị viên FSCM", SystemRoles.Admin),
        ("operator@fscm.local", "Nhân viên vận hành FSCM", SystemRoles.Operator),
        ("sales.manager@fscm.local", "Quản lý bán hàng FSCM", SystemRoles.SalesManager),
        ("sales@fscm.local", "Nhân viên bán hàng FSCM", SystemRoles.Sales),
        ("warehouse.keeper@fscm.local", "Thủ kho FSCM", SystemRoles.WarehouseKeeper),
        ("retailer@fscm.local", "Người dùng nhà bán lẻ FSCM", SystemRoles.Retailer)
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
        var categories = await SeedCategoriesAsync(now, cancellationToken);
        var products = await SeedProductsAsync(categories, now, cancellationToken);

        await SeedWarehouseAssignmentsAsync(users, warehouse.Id, now, cancellationToken);
        await SeedSalesGroupMembershipsAsync(users, salesGroup.Id, now, cancellationToken);
        await SeedRetailerAssignmentAsync(retailer.Id, salesGroup.Id, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Development data seeded: {UserCount} users, {CategoryCount} categories, {ProductCount} products, warehouse {WarehouseCode}, sales group {SalesGroupCode}, retailer {RetailerCode}.",
            users.Count,
            categories.Count,
            products.Count,
            DemoWarehouseCode,
            DemoSalesGroupCode,
            DemoRetailerCode);
    }

    private async Task<Dictionary<string, ProductCategory>> SeedCategoriesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new CategorySeed("CAT-01", "Đồ uống", "Đồ uống có hạn sử dụng ngắn", (string?)null),
            new CategorySeed("CAT-02", "Sữa và sản phẩm từ sữa", "Sữa và các sản phẩm sữa lên men", "CAT-01"),
            new CategorySeed("CAT-03", "Thực phẩm tươi", "Thực phẩm tươi được đóng gói", (string?)null),
            new CategorySeed("CAT-04", "Hàng theo mùa", "Danh mục ngừng hoạt động dùng để kiểm thử", (string?)null, false)
        };
        var categories = new Dictionary<string, ProductCategory>(StringComparer.Ordinal);

        foreach (var definition in definitions.Where(item => item.ParentCode is null))
        {
            categories[definition.Code] = await UpsertCategoryAsync(definition, null, now, cancellationToken);
        }

        foreach (var definition in definitions.Where(item => item.ParentCode is not null))
        {
            categories[definition.Code] = await UpsertCategoryAsync(
                definition,
                categories[definition.ParentCode!].Id,
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return categories;
    }

    private async Task<ProductCategory> UpsertCategoryAsync(
        CategorySeed definition,
        Guid? parentCategoryId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var category = await dbContext.ProductCategoryRecords.SingleOrDefaultAsync(
            candidate => candidate.Code == definition.Code,
            cancellationToken);
        if (category is null)
        {
            category = new ProductCategory
            {
                Id = Guid.NewGuid(),
                Code = definition.Code,
                CreatedAt = now
            };
            dbContext.ProductCategoryRecords.Add(category);
        }

        category.ParentCategoryId = parentCategoryId;
        category.Name = definition.Name;
        category.Description = definition.Description;
        category.IsActive = definition.IsActive;
        category.UpdatedAt = now;
        return category;
    }

    private async Task<Dictionary<string, Product>> SeedProductsAsync(
        IReadOnlyDictionary<string, ProductCategory> categories,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new ProductSeed("SKU001", "Sữa tươi 1 lít", "Thùng", 168, 24, "CAT-02", ProductStatuses.Published),
            new ProductSeed("SKU002", "Sữa chua uống 180 ml", "Thùng", 336, 48, "CAT-02", ProductStatuses.Published),
            new ProductSeed("SKU003", "Bánh mì kẹp tươi", "Khay", 72, 12, "CAT-03", ProductStatuses.Draft),
            new ProductSeed("SKU004", "Nước cam 250 ml - thùng 48 hộp", "Thùng", 720, 120, "CAT-01", ProductStatuses.Inactive),
            new ProductSeed("SKU005", "Nước cam 250 ml - thùng 36 hộp", "Thùng", 720, 120, "CAT-01", ProductStatuses.Published, "SKU004")
        };
        var products = new Dictionary<string, Product>(StringComparer.Ordinal);

        foreach (var definition in definitions.Where(item => item.ReplacesSku is null))
        {
            products[definition.Sku] = await UpsertProductAsync(
                definition,
                categories[definition.CategoryCode].Id,
                null,
                now,
                cancellationToken);
        }

        foreach (var definition in definitions.Where(item => item.ReplacesSku is not null))
        {
            products[definition.Sku] = await UpsertProductAsync(
                definition,
                categories[definition.CategoryCode].Id,
                products[definition.ReplacesSku!].Id,
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedPriceHistoryAsync(products["SKU001"], [235_000m, 245_000m, 252_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU002"], [310_000m, 320_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU003"], [180_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU004"], [400_000m, 415_000m], now, cancellationToken);
        await SeedPriceHistoryAsync(products["SKU005"], [325_000m], now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return products;
    }

    private async Task<Product> UpsertProductAsync(
        ProductSeed definition,
        Guid categoryId,
        Guid? replacesProductId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.ProductRecords.SingleOrDefaultAsync(
            candidate => candidate.Sku == definition.Sku,
            cancellationToken);
        if (product is null)
        {
            product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = definition.Sku,
                CreatedAt = now.AddDays(-90)
            };
            dbContext.ProductRecords.Add(product);
        }

        product.ProductCategoryId = categoryId;
        product.ReplacesProductId = replacesProductId;
        product.Name = definition.Name;
        product.Unit = definition.Unit;
        product.ShelfLifeHours = definition.ShelfLifeHours;
        product.NearDateThresholdHours = definition.NearDateThresholdHours;
        product.Status = definition.Status;
        product.PublishedAt = definition.Status is ProductStatuses.Published or ProductStatuses.Inactive
            ? now.AddDays(-60)
            : null;
        product.InactivatedAt = definition.Status == ProductStatuses.Inactive ? now.AddDays(-15) : null;
        product.UpdatedAt = now;
        return product;
    }

    private async Task SeedPriceHistoryAsync(
        Product product,
        IReadOnlyList<decimal> prices,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ProductPriceRecords.AnyAsync(
                candidate => candidate.ProductId == product.Id,
                cancellationToken))
        {
            return;
        }

        for (var index = 0; index < prices.Count; index++)
        {
            var effectiveFrom = now.AddDays(-30 * (prices.Count - index));
            var isLast = index == prices.Count - 1;
            var isActive = isLast && product.Status != ProductStatuses.Inactive;
            dbContext.ProductPriceRecords.Add(new ProductPrice
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                UnitPrice = prices[index],
                EffectiveFrom = effectiveFrom,
                EffectiveTo = isActive
                    ? null
                    : isLast
                        ? product.InactivatedAt ?? now
                        : now.AddDays(-30 * (prices.Count - index - 1)),
                IsActive = isActive,
                CreatedAt = effectiveFrom
            });
        }
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
            Name = "Thành phố Hồ Chí Minh",
            Description = "Khu vực dữ liệu mẫu dùng cho môi trường phát triển",
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
            Name = "Kho trung tâm Củ Chi",
            Address = "Huyện Củ Chi, Thành phố Hồ Chí Minh",
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
            Name = "Nhóm bán hàng Thành phố Hồ Chí Minh 01",
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
            Name = "Cửa hàng bán lẻ mẫu FSCM",
            ContactName = "Nguyễn Văn Minh",
            Phone = "+84 28 0000 0001",
            Email = "store@fscm.local",
            Address = "Quận 1, Thành phố Hồ Chí Minh",
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

    private sealed record CategorySeed(
        string Code,
        string Name,
        string Description,
        string? ParentCode,
        bool IsActive = true);

    private sealed record ProductSeed(
        string Sku,
        string Name,
        string Unit,
        int ShelfLifeHours,
        int NearDateThresholdHours,
        string CategoryCode,
        string Status,
        string? ReplacesSku = null);
}

