using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailerManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "RetailerBusinessCodeSequence",
                startValue: 10005000L);

            migrationBuilder.CreateSequence(
                name: "RetailerDeclarationCodeSequence",
                startValue: 100L);

            migrationBuilder.AddColumn<int>(
                name: "CreditTermDays",
                table: "RETAILER",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseFrequencyPerMonth",
                table: "RETAILER",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RetailerType",
                table: "RETAILER",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "GroceryStore");

            migrationBuilder.AddColumn<string>(
                name: "StoreSizeCategory",
                table: "RETAILER",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "M");

            migrationBuilder.Sql("""
                UPDATE "RETAILER"
                SET "CreditTermDays" = CASE WHEN "CreditTermDays" IN (0, 7, 14, 30) THEN "CreditTermDays" ELSE 0 END,
                    "PurchaseFrequencyPerMonth" = CASE WHEN "PurchaseFrequencyPerMonth" > 0 THEN "PurchaseFrequencyPerMonth" ELSE 4 END,
                    "RetailerType" = CASE WHEN LENGTH(TRIM("RetailerType")) > 0 THEN "RetailerType" ELSE 'GroceryStore' END,
                    "StoreSizeCategory" = CASE
                        WHEN "StoreSizeSquareMeters" < 30 THEN 'S'
                        WHEN "StoreSizeSquareMeters" > 60 THEN 'L'
                        ELSE 'M'
                    END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RETAILER_CreditTermDays",
                table: "RETAILER",
                sql: "\"CreditTermDays\" IN (0, 7, 14, 30)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RETAILER_PurchaseFrequencyPerMonth",
                table: "RETAILER",
                sql: "\"PurchaseFrequencyPerMonth\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RETAILER_StoreSizeCategory",
                table: "RETAILER",
                sql: "\"StoreSizeCategory\" IN ('S', 'M', 'L')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RETAILER_CreditTermDays",
                table: "RETAILER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RETAILER_PurchaseFrequencyPerMonth",
                table: "RETAILER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RETAILER_StoreSizeCategory",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "CreditTermDays",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "PurchaseFrequencyPerMonth",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "RetailerType",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "StoreSizeCategory",
                table: "RETAILER");

            migrationBuilder.DropSequence(
                name: "RetailerBusinessCodeSequence");

            migrationBuilder.DropSequence(
                name: "RetailerDeclarationCodeSequence");
        }
    }
}
