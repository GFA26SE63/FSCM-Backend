using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.IntegrationTests.Persistence;

public sealed class PhysicalSchemaModelTests
{
    private static readonly string[] ExpectedTables =
    [
        "ALLOCATION",
        "AUDIT_LOG",
        "BATCH",
        "BATCH_PROMOTION",
        "BATCH_QR_LABEL",
        "DELIVERY",
        "DRIVER",
        "GOODS_RECEIPT",
        "GOODS_RECEIPT_LINE",
        "IDEMPOTENCY_KEY",
        "INVENTORY_BALANCE",
        "LOYALTY_PROGRAM",
        "LOYALTY_REWARD",
        "LOYALTY_TRANSACTION",
        "MAP_LOCATION",
        "NOTIFICATION",
        "ORDER_PROMOTION",
        "ORDER_STATUS_HISTORY",
        "OUTBOX_MESSAGE",
        "PAYMENT",
        "PICKING_ITEM",
        "PICKING_LIST",
        "PRODUCT",
        "PRODUCT_CATEGORY",
        "PRODUCT_PRICE",
        "PROMOTION",
        "PROMOTION_CATEGORY",
        "PROMOTION_PRODUCT",
        "PROMOTION_RETAILER",
        "RECEIPT_CONFIRMATION",
        "RECEIPT_CONFIRMATION_ITEM",
        "RECEIPT_DISCREPANCY",
        "RECEIPT_DISCREPANCY_ITEM",
        "REGION",
        "RESERVATION",
        "RETAILER",
        "RETAILER_LOYALTY",
        "RETAILER_PRODUCT_MLOR",
        "RETAILER_SALES_ASSIGNMENT",
        "REWARD_REDEMPTION",
        "SALES_GROUP",
        "SALES_GROUP_MEMBER",
        "SALES_ORDER",
        "SALES_ORDER_ATTACHMENT",
        "SALES_ORDER_LINE",
        "STOCK_HOLD",
        "SYSTEM_CONFIGURATION",
        "TRANSIT_TIME_RULE",
        "USER_ACCOUNT",
        "USER_WAREHOUSE_ASSIGNMENT",
        "VEHICLE",
        "WAREHOUSE",
        "WAREHOUSE_TRANSFER",
        "WAREHOUSE_TRANSFER_ITEM",
        "WRITE_OFF",
        "WRITE_OFF_ATTACHMENT",
        "WRITE_OFF_ITEM"
    ];

    [Fact]
    public void Model_matches_approved_physical_erd_shape()
    {
        using var context = CreateContext();

        var entityTypes = context.Model.GetEntityTypes()
            .Where(entityType => ExpectedTables.Contains(entityType.GetTableName(), StringComparer.Ordinal))
            .ToArray();
        var tableNames = entityTypes
            .Select(entityType => entityType.GetTableName())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTables, tableNames);
        Assert.Equal(57, entityTypes.Length);
        Assert.Equal(515, entityTypes.Sum(entityType => entityType.GetProperties().Count()));
        Assert.Equal(116, entityTypes.Sum(entityType => entityType.GetForeignKeys().Count()));
        Assert.All(
            entityTypes.SelectMany(entityType => entityType.GetForeignKeys()),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void Model_contains_identity_authentication_tables()
    {
        using var context = CreateContext();

        var identityTables = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is "ACCOUNT_TOKEN" or "REFRESH_TOKEN" or "USER_CREDENTIAL")
            .Select(tableName => tableName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ACCOUNT_TOKEN", "REFRESH_TOKEN", "USER_CREDENTIAL"], identityTables);

        var credential = context.Model.FindEntityType(typeof(UserCredential));
        var userAccountId = credential!.FindProperty(nameof(UserCredential.UserAccountId));
        Assert.True(credential.GetIndexes().Single(index => index.Properties.Contains(userAccountId!)).IsUnique);

        var refreshToken = context.Model.FindEntityType(typeof(RefreshToken));
        var tokenHash = refreshToken!.FindProperty(nameof(RefreshToken.TokenHash));
        Assert.True(refreshToken.GetIndexes().Single(index => index.Properties.Contains(tokenHash!)).IsUnique);
    }

    [Fact]
    public void Model_preserves_representative_postgresql_facets()
    {
        using var context = CreateContext();

        var product = context.Model.FindEntityType(typeof(Product));
        var sku = product!.FindProperty(nameof(Product.Sku));
        Assert.Equal(50, sku!.GetMaxLength());
        Assert.True(product.GetIndexes().Single(index => index.Properties.Contains(sku)).IsUnique);
        var replacesProductId = product.FindProperty(nameof(Product.ReplacesProductId));
        Assert.True(product.GetIndexes().Single(index => index.Properties.Contains(replacesProductId!)).IsUnique);

        var inventoryBalance = context.Model.FindEntityType(typeof(InventoryBalance));
        var physicalQuantity = inventoryBalance!.FindProperty(nameof(InventoryBalance.PhysicalQuantity));
        Assert.Equal(18, physicalQuantity!.GetPrecision());
        Assert.Equal(3, physicalQuantity.GetScale());

        var auditLog = context.Model.FindEntityType(typeof(AuditLog));
        Assert.Equal("jsonb", auditLog!.FindProperty(nameof(AuditLog.OldValues))!.GetColumnType());
        Assert.Equal("jsonb", auditLog.FindProperty(nameof(AuditLog.NewValues))!.GetColumnType());
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=fscm_model_validation;Username=fscm;Password=not-used")
            .Options;

        return new AppDbContext(options);
    }
}
