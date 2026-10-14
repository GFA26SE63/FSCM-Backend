using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPhysicalSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DRIVER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LicenseNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DRIVER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OUTBOX_MESSAGE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OUTBOX_MESSAGE", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRODUCT_CATEGORY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRODUCT_CATEGORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRODUCT_CATEGORY_PRODUCT_CATEGORY_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "PRODUCT_CATEGORY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REGION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REGION", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "USER_ACCOUNT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntraObjectId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    FullName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ACCOUNT", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VEHICLE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Capacity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VEHICLE", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PRODUCT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShelfLifeHours = table.Column<int>(type: "integer", nullable: false),
                    NearDateThresholdHours = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRODUCT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRODUCT_PRODUCT_CATEGORY_ProductCategoryId",
                        column: x => x.ProductCategoryId,
                        principalTable: "PRODUCT_CATEGORY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SALES_GROUP",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_GROUP", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SALES_GROUP_REGION_RegionId",
                        column: x => x.RegionId,
                        principalTable: "REGION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AUDIT_LOG",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AUDIT_LOG", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AUDIT_LOG_USER_ACCOUNT_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LOYALTY_PROGRAM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SpendAmountPerPoint = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MinimumRedeemPoints = table.Column<int>(type: "integer", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LOYALTY_PROGRAM", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LOYALTY_PROGRAM_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROMOTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PromotionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MinimumQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROMOTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PROMOTION_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RETAILER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ForecastRevenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    StoreSizeSquareMeters = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    PurchasingCapacity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PotentialStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PotentialScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    AssessmentNote = table.Column<string>(type: "text", nullable: true),
                    DeclaredByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssessedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RETAILER_REGION_RegionId",
                        column: x => x.RegionId,
                        principalTable: "REGION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RETAILER_USER_ACCOUNT_AssessedByUserId",
                        column: x => x.AssessedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RETAILER_USER_ACCOUNT_DeclaredByUserId",
                        column: x => x.DeclaredByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RETAILER_USER_ACCOUNT_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SYSTEM_CONFIGURATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ConfigValue = table.Column<string>(type: "jsonb", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ValueType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SYSTEM_CONFIGURATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SYSTEM_CONFIGURATION_USER_ACCOUNT_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GOODS_RECEIPT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GOODS_RECEIPT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GOODS_RECEIPT_USER_ACCOUNT_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GOODS_RECEIPT_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GOODS_RECEIPT_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TRANSIT_TIME_RULE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinDistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    MaxDistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    HandlingMinutes = table.Column<int>(type: "integer", nullable: false),
                    TransitMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxServingDistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSIT_TIME_RULE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TRANSIT_TIME_RULE_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "USER_WAREHOUSE_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_WAREHOUSE_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_USER_WAREHOUSE_ASSIGNMENT_USER_ACCOUNT_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_USER_WAREHOUSE_ASSIGNMENT_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_TRANSFER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_USER_ACCOUNT_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_WAREHOUSE_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_WAREHOUSE_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WRITE_OFF",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WriteOffCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WRITE_OFF", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WRITE_OFF_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WRITE_OFF_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PRODUCT_PRICE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRODUCT_PRICE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PRODUCT_PRICE_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SALES_GROUP_MEMBER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnassignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_GROUP_MEMBER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SALES_GROUP_MEMBER_SALES_GROUP_SalesGroupId",
                        column: x => x.SalesGroupId,
                        principalTable: "SALES_GROUP",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SALES_GROUP_MEMBER_USER_ACCOUNT_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LOYALTY_REWARD",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoyaltyProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RewardType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequiredPoints = table.Column<int>(type: "integer", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    AvailableQuantity = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LOYALTY_REWARD", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LOYALTY_REWARD_LOYALTY_PROGRAM_LoyaltyProgramId",
                        column: x => x.LoyaltyProgramId,
                        principalTable: "LOYALTY_PROGRAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROMOTION_CATEGORY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROMOTION_CATEGORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PROMOTION_CATEGORY_PRODUCT_CATEGORY_ProductCategoryId",
                        column: x => x.ProductCategoryId,
                        principalTable: "PRODUCT_CATEGORY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PROMOTION_CATEGORY_PROMOTION_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "PROMOTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROMOTION_PRODUCT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROMOTION_PRODUCT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PROMOTION_PRODUCT_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PROMOTION_PRODUCT_PROMOTION_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "PROMOTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MAP_LOCATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    FormattedAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProviderPlaceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MAP_LOCATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MAP_LOCATION_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MAP_LOCATION_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROMOTION_RETAILER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROMOTION_RETAILER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PROMOTION_RETAILER_PROMOTION_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "PROMOTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PROMOTION_RETAILER_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RETAILER_LOYALTY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentPoints = table.Column<int>(type: "integer", nullable: false),
                    LifetimeEarnedPoints = table.Column<int>(type: "integer", nullable: false),
                    LifetimeRedeemedPoints = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER_LOYALTY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RETAILER_LOYALTY_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RETAILER_PRODUCT_MLOR",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumLifeRatio = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    MinimumLifeHours = table.Column<int>(type: "integer", nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER_PRODUCT_MLOR", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RETAILER_PRODUCT_MLOR_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RETAILER_PRODUCT_MLOR_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RETAILER_SALES_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnassignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER_SALES_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RETAILER_SALES_ASSIGNMENT_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RETAILER_SALES_ASSIGNMENT_SALES_GROUP_SalesGroupId",
                        column: x => x.SalesGroupId,
                        principalTable: "SALES_GROUP",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SALES_ORDER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientOrderKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "text", nullable: false),
                    SubtotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OfflineEstimatedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    HasOfflineDifference = table.Column<bool>(type: "boolean", nullable: false),
                    ServerReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_ORDER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_SALES_GROUP_SalesGroupId",
                        column: x => x.SalesGroupId,
                        principalTable: "SALES_GROUP",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_USER_ACCOUNT_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GOODS_RECEIPT_LINE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLotCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpirationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GOODS_RECEIPT_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GOODS_RECEIPT_LINE_GOODS_RECEIPT_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GOODS_RECEIPT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GOODS_RECEIPT_LINE_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WRITE_OFF_ATTACHMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WriteOffId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BlobUrl = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WRITE_OFF_ATTACHMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WRITE_OFF_ATTACHMENT_USER_ACCOUNT_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WRITE_OFF_ATTACHMENT_WRITE_OFF_WriteOffId",
                        column: x => x.WriteOffId,
                        principalTable: "WRITE_OFF",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DELIVERY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DELIVERY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DELIVERY_DRIVER_DriverId",
                        column: x => x.DriverId,
                        principalTable: "DRIVER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DELIVERY_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DELIVERY_VEHICLE_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "VEHICLE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IDEMPOTENCY_KEY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IDEMPOTENCY_KEY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IDEMPOTENCY_KEY_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IDEMPOTENCY_KEY_USER_ACCOUNT_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LOYALTY_TRANSACTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoyaltyProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerLoyaltyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LOYALTY_TRANSACTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LOYALTY_TRANSACTION_LOYALTY_PROGRAM_LoyaltyProgramId",
                        column: x => x.LoyaltyProgramId,
                        principalTable: "LOYALTY_PROGRAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LOYALTY_TRANSACTION_RETAILER_LOYALTY_RetailerLoyaltyId",
                        column: x => x.RetailerLoyaltyId,
                        principalTable: "RETAILER_LOYALTY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LOYALTY_TRANSACTION_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LOYALTY_TRANSACTION_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NOTIFICATION_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NOTIFICATION_USER_ACCOUNT_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ORDER_STATUS_HISTORY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORDER_STATUS_HISTORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ORDER_STATUS_HISTORY_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ORDER_STATUS_HISTORY_USER_ACCOUNT_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PAYMENT_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PAYMENT_USER_ACCOUNT_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PICKING_LIST",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickingCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PICKING_LIST", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PICKING_LIST_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PICKING_LIST_USER_ACCOUNT_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PICKING_LIST_USER_ACCOUNT_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PICKING_LIST_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RECEIPT_CONFIRMATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedByRetailerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECEIPT_CONFIRMATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RECEIPT_CONFIRMATION_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_CONFIRMATION_USER_ACCOUNT_ConfirmedByRetailerUserId",
                        column: x => x.ConfirmedByRetailerUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SALES_ORDER_ATTACHMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttachmentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BlobUrl = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_ORDER_ATTACHMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_ATTACHMENT_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_ATTACHMENT_USER_ACCOUNT_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SALES_ORDER_LINE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EstimatedArrivalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_ORDER_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_LINE_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SALES_ORDER_LINE_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BATCH",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RootBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    BatchCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpirationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiryStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StockStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BATCH", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BATCH_BATCH_ParentBatchId",
                        column: x => x.ParentBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_BATCH_RootBatchId",
                        column: x => x.RootBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_GOODS_RECEIPT_LINE_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "GOODS_RECEIPT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REWARD_REDEMPTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoyaltyRewardId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoyaltyTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RedeemedPoints = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RedeemedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REWARD_REDEMPTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REWARD_REDEMPTION_LOYALTY_REWARD_LoyaltyRewardId",
                        column: x => x.LoyaltyRewardId,
                        principalTable: "LOYALTY_REWARD",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REWARD_REDEMPTION_LOYALTY_TRANSACTION_LoyaltyTransactionId",
                        column: x => x.LoyaltyTransactionId,
                        principalTable: "LOYALTY_TRANSACTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REWARD_REDEMPTION_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REWARD_REDEMPTION_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REWARD_REDEMPTION_USER_ACCOUNT_RedeemedByUserId",
                        column: x => x.RedeemedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RECEIPT_DISCREPANCY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptConfirmationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedByRetailerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECEIPT_DISCREPANCY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RECEIPT_DISCREPANCY_RECEIPT_CONFIRMATION_ReceiptConfirmatio~",
                        column: x => x.ReceiptConfirmationId,
                        principalTable: "RECEIPT_CONFIRMATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_DISCREPANCY_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_DISCREPANCY_USER_ACCOUNT_ReportedByRetailerUserId",
                        column: x => x.ReportedByRetailerUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_DISCREPANCY_USER_ACCOUNT_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ORDER_PROMOTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsServerFinal = table.Column<bool>(type: "boolean", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORDER_PROMOTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ORDER_PROMOTION_PROMOTION_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "PROMOTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ORDER_PROMOTION_SALES_ORDER_LINE_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SALES_ORDER_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALLOCATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    AllocationSequence = table.Column<int>(type: "integer", nullable: false),
                    DistanceMeters = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    EstimatedArrivalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemainingLifeHoursAtArrival = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_SALES_ORDER_LINE_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SALES_ORDER_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BATCH_PROMOTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BATCH_PROMOTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BATCH_PROMOTION_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_PROMOTION_PROMOTION_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "PROMOTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_PROMOTION_USER_ACCOUNT_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BATCH_QR_LABEL",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    QrCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BlobUrl = table.Column<string>(type: "text", nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BATCH_QR_LABEL", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BATCH_QR_LABEL_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "INVENTORY_BALANCE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhysicalQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    HeldQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVENTORY_BALANCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_INVENTORY_BALANCE_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER_ITEM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DispatchedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_TRANSFER_ITEM", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_ITEM_BATCH_DestinationBatchId",
                        column: x => x.DestinationBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_ITEM_BATCH_SourceBatchId",
                        column: x => x.SourceBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_ITEM_WAREHOUSE_TRANSFER_WarehouseTransfe~",
                        column: x => x.WarehouseTransferId,
                        principalTable: "WAREHOUSE_TRANSFER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WRITE_OFF_ITEM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WriteOffId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WRITE_OFF_ITEM", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WRITE_OFF_ITEM_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WRITE_OFF_ITEM_WRITE_OFF_WriteOffId",
                        column: x => x.WriteOffId,
                        principalTable: "WRITE_OFF",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RECEIPT_DISCREPANCY_ITEM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptDiscrepancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssueType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ActualReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    EvidenceBlobUrl = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECEIPT_DISCREPANCY_ITEM", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RECEIPT_DISCREPANCY_ITEM_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_DISCREPANCY_ITEM_RECEIPT_DISCREPANCY_ReceiptDiscrep~",
                        column: x => x.ReceiptDiscrepancyId,
                        principalTable: "RECEIPT_DISCREPANCY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PICKING_ITEM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickingListId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockOutBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PICKING_ITEM", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PICKING_ITEM_ALLOCATION_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "ALLOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PICKING_ITEM_BATCH_SourceBatchId",
                        column: x => x.SourceBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PICKING_ITEM_BATCH_StockOutBatchId",
                        column: x => x.StockOutBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PICKING_ITEM_PICKING_LIST_PickingListId",
                        column: x => x.PickingListId,
                        principalTable: "PICKING_LIST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RESERVATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RESERVATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RESERVATION_ALLOCATION_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "ALLOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RESERVATION_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "STOCK_HOLD",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    HeldAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STOCK_HOLD", x => x.Id);
                    table.ForeignKey(
                        name: "FK_STOCK_HOLD_ALLOCATION_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "ALLOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_STOCK_HOLD_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RECEIPT_CONFIRMATION_ITEM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptConfirmationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchQrLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECEIPT_CONFIRMATION_ITEM", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RECEIPT_CONFIRMATION_ITEM_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_CONFIRMATION_ITEM_BATCH_QR_LABEL_BatchQrLabelId",
                        column: x => x.BatchQrLabelId,
                        principalTable: "BATCH_QR_LABEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_CONFIRMATION_ITEM_RECEIPT_CONFIRMATION_ReceiptConfi~",
                        column: x => x.ReceiptConfirmationId,
                        principalTable: "RECEIPT_CONFIRMATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_BatchId",
                table: "ALLOCATION",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_SalesOrderLineId",
                table: "ALLOCATION",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_WarehouseId",
                table: "ALLOCATION",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_AUDIT_LOG_UserAccountId",
                table: "AUDIT_LOG",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_BatchCode",
                table: "BATCH",
                column: "BatchCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_GoodsReceiptLineId",
                table: "BATCH",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_ParentBatchId",
                table: "BATCH",
                column: "ParentBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_ProductId",
                table: "BATCH",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_RootBatchId",
                table: "BATCH",
                column: "RootBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId",
                table: "BATCH",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_AssignedByUserId",
                table: "BATCH_PROMOTION",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_BatchId",
                table: "BATCH_PROMOTION",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_PromotionId",
                table: "BATCH_PROMOTION",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_QR_LABEL_BatchId",
                table: "BATCH_QR_LABEL",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_QR_LABEL_QrCode",
                table: "BATCH_QR_LABEL",
                column: "QrCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_DriverId",
                table: "DELIVERY",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_SalesOrderId",
                table: "DELIVERY",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_VehicleId",
                table: "DELIVERY",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_GOODS_RECEIPT_ApprovedByUserId",
                table: "GOODS_RECEIPT",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GOODS_RECEIPT_CreatedByUserId",
                table: "GOODS_RECEIPT",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GOODS_RECEIPT_ReceiptCode",
                table: "GOODS_RECEIPT",
                column: "ReceiptCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GOODS_RECEIPT_WarehouseId",
                table: "GOODS_RECEIPT",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_GOODS_RECEIPT_LINE_GoodsReceiptId",
                table: "GOODS_RECEIPT_LINE",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_GOODS_RECEIPT_LINE_ProductId",
                table: "GOODS_RECEIPT_LINE",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_IDEMPOTENCY_KEY_Key",
                table: "IDEMPOTENCY_KEY",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IDEMPOTENCY_KEY_SalesOrderId",
                table: "IDEMPOTENCY_KEY",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IDEMPOTENCY_KEY_UserAccountId",
                table: "IDEMPOTENCY_KEY",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_BALANCE_BatchId",
                table: "INVENTORY_BALANCE",
                column: "BatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_PROGRAM_Code",
                table: "LOYALTY_PROGRAM",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_PROGRAM_CreatedByUserId",
                table: "LOYALTY_PROGRAM",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_REWARD_Code",
                table: "LOYALTY_REWARD",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_REWARD_LoyaltyProgramId",
                table: "LOYALTY_REWARD",
                column: "LoyaltyProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_TRANSACTION_CreatedByUserId",
                table: "LOYALTY_TRANSACTION",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_TRANSACTION_LoyaltyProgramId",
                table: "LOYALTY_TRANSACTION",
                column: "LoyaltyProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_TRANSACTION_RetailerLoyaltyId",
                table: "LOYALTY_TRANSACTION",
                column: "RetailerLoyaltyId");

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_TRANSACTION_SalesOrderId",
                table: "LOYALTY_TRANSACTION",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MAP_LOCATION_RetailerId",
                table: "MAP_LOCATION",
                column: "RetailerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MAP_LOCATION_WarehouseId",
                table: "MAP_LOCATION",
                column: "WarehouseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_SalesOrderId",
                table: "NOTIFICATION",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_UserAccountId",
                table: "NOTIFICATION",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_PROMOTION_PromotionId",
                table: "ORDER_PROMOTION",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_PROMOTION_SalesOrderLineId",
                table: "ORDER_PROMOTION",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_STATUS_HISTORY_ChangedByUserId",
                table: "ORDER_STATUS_HISTORY",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_STATUS_HISTORY_SalesOrderId",
                table: "ORDER_STATUS_HISTORY",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_ConfirmedByUserId",
                table: "PAYMENT",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_SalesOrderId",
                table: "PAYMENT",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEM_AllocationId",
                table: "PICKING_ITEM",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEM_PickingListId",
                table: "PICKING_ITEM",
                column: "PickingListId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEM_SourceBatchId",
                table: "PICKING_ITEM",
                column: "SourceBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEM_StockOutBatchId",
                table: "PICKING_ITEM",
                column: "StockOutBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LIST_AssignedToUserId",
                table: "PICKING_LIST",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LIST_ConfirmedByUserId",
                table: "PICKING_LIST",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LIST_PickingCode",
                table: "PICKING_LIST",
                column: "PickingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LIST_SalesOrderId",
                table: "PICKING_LIST",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LIST_WarehouseId",
                table: "PICKING_LIST",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_ProductCategoryId",
                table: "PRODUCT",
                column: "ProductCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_Sku",
                table: "PRODUCT",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_CATEGORY_Code",
                table: "PRODUCT_CATEGORY",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_CATEGORY_ParentCategoryId",
                table: "PRODUCT_CATEGORY",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_PRICE_ProductId",
                table: "PRODUCT_PRICE",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_Code",
                table: "PROMOTION",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_CreatedByUserId",
                table: "PROMOTION",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_CATEGORY_ProductCategoryId",
                table: "PROMOTION_CATEGORY",
                column: "ProductCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_CATEGORY_PromotionId",
                table: "PROMOTION_CATEGORY",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_PRODUCT_ProductId",
                table: "PROMOTION_PRODUCT",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_PRODUCT_PromotionId",
                table: "PROMOTION_PRODUCT",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_RETAILER_PromotionId",
                table: "PROMOTION_RETAILER",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_RETAILER_RetailerId",
                table: "PROMOTION_RETAILER",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ConfirmedByRetailerUserId",
                table: "RECEIPT_CONFIRMATION",
                column: "ConfirmedByRetailerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_SalesOrderId",
                table: "RECEIPT_CONFIRMATION",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_BatchId",
                table: "RECEIPT_CONFIRMATION_ITEM",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_BatchQrLabelId",
                table: "RECEIPT_CONFIRMATION_ITEM",
                column: "BatchQrLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_ReceiptConfirmationId",
                table: "RECEIPT_CONFIRMATION_ITEM",
                column: "ReceiptConfirmationId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ReceiptConfirmationId",
                table: "RECEIPT_DISCREPANCY",
                column: "ReceiptConfirmationId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ReportedByRetailerUserId",
                table: "RECEIPT_DISCREPANCY",
                column: "ReportedByRetailerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ReviewedByUserId",
                table: "RECEIPT_DISCREPANCY",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_SalesOrderId",
                table: "RECEIPT_DISCREPANCY",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ITEM_BatchId",
                table: "RECEIPT_DISCREPANCY_ITEM",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ITEM_ReceiptDiscrepancyId",
                table: "RECEIPT_DISCREPANCY_ITEM",
                column: "ReceiptDiscrepancyId");

            migrationBuilder.CreateIndex(
                name: "IX_REGION_Code",
                table: "REGION",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RESERVATION_BatchId",
                table: "RESERVATION",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_AssessedByUserId",
                table: "RETAILER",
                column: "AssessedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_Code",
                table: "RETAILER",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DeclaredByUserId",
                table: "RETAILER",
                column: "DeclaredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_RegionId",
                table: "RETAILER",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_ReviewedByUserId",
                table: "RETAILER",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_LOYALTY_RetailerId",
                table: "RETAILER_LOYALTY",
                column: "RetailerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_ProductId",
                table: "RETAILER_PRODUCT_MLOR",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId",
                table: "RETAILER_PRODUCT_MLOR",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_RetailerId",
                table: "RETAILER_SALES_ASSIGNMENT",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_SalesGroupId",
                table: "RETAILER_SALES_ASSIGNMENT",
                column: "SalesGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_REWARD_REDEMPTION_LoyaltyRewardId",
                table: "REWARD_REDEMPTION",
                column: "LoyaltyRewardId");

            migrationBuilder.CreateIndex(
                name: "IX_REWARD_REDEMPTION_LoyaltyTransactionId",
                table: "REWARD_REDEMPTION",
                column: "LoyaltyTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REWARD_REDEMPTION_RedeemedByUserId",
                table: "REWARD_REDEMPTION",
                column: "RedeemedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REWARD_REDEMPTION_RetailerId",
                table: "REWARD_REDEMPTION",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_REWARD_REDEMPTION_SalesOrderId",
                table: "REWARD_REDEMPTION",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_Code",
                table: "SALES_GROUP",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_RegionId",
                table: "SALES_GROUP",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_MEMBER_SalesGroupId",
                table: "SALES_GROUP_MEMBER",
                column: "SalesGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_MEMBER_UserAccountId",
                table: "SALES_GROUP_MEMBER",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_ClientOrderKey",
                table: "SALES_ORDER",
                column: "ClientOrderKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_CreatedByUserId",
                table: "SALES_ORDER",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_OrderCode",
                table: "SALES_ORDER",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_RetailerId",
                table: "SALES_ORDER",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_ReviewedByUserId",
                table: "SALES_ORDER",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_SalesGroupId",
                table: "SALES_ORDER",
                column: "SalesGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_ATTACHMENT_SalesOrderId",
                table: "SALES_ORDER_ATTACHMENT",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_ATTACHMENT_UploadedByUserId",
                table: "SALES_ORDER_ATTACHMENT",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_ProductId",
                table: "SALES_ORDER_LINE",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId",
                table: "SALES_ORDER_LINE",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_STOCK_HOLD_AllocationId",
                table: "STOCK_HOLD",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_STOCK_HOLD_BatchId",
                table: "STOCK_HOLD",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_CONFIGURATION_ConfigKey",
                table: "SYSTEM_CONFIGURATION",
                column: "ConfigKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_CONFIGURATION_UpdatedByUserId",
                table: "SYSTEM_CONFIGURATION",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TIME_RULE_WarehouseId",
                table: "TRANSIT_TIME_RULE",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_Email",
                table: "USER_ACCOUNT",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_EntraObjectId",
                table: "USER_ACCOUNT",
                column: "EntraObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_WAREHOUSE_ASSIGNMENT_UserAccountId",
                table: "USER_WAREHOUSE_ASSIGNMENT",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_USER_WAREHOUSE_ASSIGNMENT_WarehouseId",
                table: "USER_WAREHOUSE_ASSIGNMENT",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_VEHICLE_PlateNumber",
                table: "VEHICLE",
                column: "PlateNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_Code",
                table: "WAREHOUSE",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ApprovedByUserId",
                table: "WAREHOUSE_TRANSFER",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_CreatedByUserId",
                table: "WAREHOUSE_TRANSFER",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_DestinationWarehouseId",
                table: "WAREHOUSE_TRANSFER",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_SourceWarehouseId",
                table: "WAREHOUSE_TRANSFER",
                column: "SourceWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_TransferCode",
                table: "WAREHOUSE_TRANSFER",
                column: "TransferCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ITEM_DestinationBatchId",
                table: "WAREHOUSE_TRANSFER_ITEM",
                column: "DestinationBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ITEM_SourceBatchId",
                table: "WAREHOUSE_TRANSFER_ITEM",
                column: "SourceBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ITEM_WarehouseTransferId",
                table: "WAREHOUSE_TRANSFER_ITEM",
                column: "WarehouseTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_WRITE_OFF_CreatedByUserId",
                table: "WRITE_OFF",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WRITE_OFF_WarehouseId",
                table: "WRITE_OFF",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WRITE_OFF_WriteOffCode",
                table: "WRITE_OFF",
                column: "WriteOffCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WRITE_OFF_ATTACHMENT_UploadedByUserId",
                table: "WRITE_OFF_ATTACHMENT",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WRITE_OFF_ATTACHMENT_WriteOffId",
                table: "WRITE_OFF_ATTACHMENT",
                column: "WriteOffId");

            migrationBuilder.CreateIndex(
                name: "IX_WRITE_OFF_ITEM_BatchId",
                table: "WRITE_OFF_ITEM",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WRITE_OFF_ITEM_WriteOffId",
                table: "WRITE_OFF_ITEM",
                column: "WriteOffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AUDIT_LOG");

            migrationBuilder.DropTable(
                name: "BATCH_PROMOTION");

            migrationBuilder.DropTable(
                name: "DELIVERY");

            migrationBuilder.DropTable(
                name: "IDEMPOTENCY_KEY");

            migrationBuilder.DropTable(
                name: "INVENTORY_BALANCE");

            migrationBuilder.DropTable(
                name: "MAP_LOCATION");

            migrationBuilder.DropTable(
                name: "NOTIFICATION");

            migrationBuilder.DropTable(
                name: "ORDER_PROMOTION");

            migrationBuilder.DropTable(
                name: "ORDER_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "OUTBOX_MESSAGE");

            migrationBuilder.DropTable(
                name: "PAYMENT");

            migrationBuilder.DropTable(
                name: "PICKING_ITEM");

            migrationBuilder.DropTable(
                name: "PRODUCT_PRICE");

            migrationBuilder.DropTable(
                name: "PROMOTION_CATEGORY");

            migrationBuilder.DropTable(
                name: "PROMOTION_PRODUCT");

            migrationBuilder.DropTable(
                name: "PROMOTION_RETAILER");

            migrationBuilder.DropTable(
                name: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropTable(
                name: "RECEIPT_DISCREPANCY_ITEM");

            migrationBuilder.DropTable(
                name: "RESERVATION");

            migrationBuilder.DropTable(
                name: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropTable(
                name: "RETAILER_SALES_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "REWARD_REDEMPTION");

            migrationBuilder.DropTable(
                name: "SALES_GROUP_MEMBER");

            migrationBuilder.DropTable(
                name: "SALES_ORDER_ATTACHMENT");

            migrationBuilder.DropTable(
                name: "STOCK_HOLD");

            migrationBuilder.DropTable(
                name: "SYSTEM_CONFIGURATION");

            migrationBuilder.DropTable(
                name: "TRANSIT_TIME_RULE");

            migrationBuilder.DropTable(
                name: "USER_WAREHOUSE_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER_ITEM");

            migrationBuilder.DropTable(
                name: "WRITE_OFF_ATTACHMENT");

            migrationBuilder.DropTable(
                name: "WRITE_OFF_ITEM");

            migrationBuilder.DropTable(
                name: "DRIVER");

            migrationBuilder.DropTable(
                name: "VEHICLE");

            migrationBuilder.DropTable(
                name: "PICKING_LIST");

            migrationBuilder.DropTable(
                name: "PROMOTION");

            migrationBuilder.DropTable(
                name: "BATCH_QR_LABEL");

            migrationBuilder.DropTable(
                name: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropTable(
                name: "LOYALTY_REWARD");

            migrationBuilder.DropTable(
                name: "LOYALTY_TRANSACTION");

            migrationBuilder.DropTable(
                name: "ALLOCATION");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER");

            migrationBuilder.DropTable(
                name: "WRITE_OFF");

            migrationBuilder.DropTable(
                name: "RECEIPT_CONFIRMATION");

            migrationBuilder.DropTable(
                name: "LOYALTY_PROGRAM");

            migrationBuilder.DropTable(
                name: "RETAILER_LOYALTY");

            migrationBuilder.DropTable(
                name: "BATCH");

            migrationBuilder.DropTable(
                name: "SALES_ORDER_LINE");

            migrationBuilder.DropTable(
                name: "GOODS_RECEIPT_LINE");

            migrationBuilder.DropTable(
                name: "SALES_ORDER");

            migrationBuilder.DropTable(
                name: "GOODS_RECEIPT");

            migrationBuilder.DropTable(
                name: "PRODUCT");

            migrationBuilder.DropTable(
                name: "RETAILER");

            migrationBuilder.DropTable(
                name: "SALES_GROUP");

            migrationBuilder.DropTable(
                name: "WAREHOUSE");

            migrationBuilder.DropTable(
                name: "PRODUCT_CATEGORY");

            migrationBuilder.DropTable(
                name: "USER_ACCOUNT");

            migrationBuilder.DropTable(
                name: "REGION");
        }
    }
}
