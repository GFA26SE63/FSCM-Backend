using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerritorySalesGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_GROUP_MEMBER_SalesGroupId",
                table: "SALES_GROUP_MEMBER");

            migrationBuilder.DropIndex(
                name: "IX_SALES_GROUP_MEMBER_UserAccountId",
                table: "SALES_GROUP_MEMBER");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_RetailerId",
                table: "RETAILER_SALES_ASSIGNMENT");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_SalesGroupId",
                table: "RETAILER_SALES_ASSIGNMENT");

            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "UserAccountId"
                        ORDER BY "AssignedAt" DESC, "Id") AS row_number
                    FROM "SALES_GROUP_MEMBER"
                    WHERE "IsActive" = TRUE
                )
                UPDATE "SALES_GROUP_MEMBER" AS membership
                SET "IsActive" = FALSE,
                    "UnassignedAt" = COALESCE(membership."UnassignedAt", CURRENT_TIMESTAMP)
                FROM ranked
                WHERE membership."Id" = ranked."Id" AND ranked.row_number > 1;
                """);

            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "RetailerId"
                        ORDER BY "AssignedAt" DESC, "Id") AS row_number
                    FROM "RETAILER_SALES_ASSIGNMENT"
                    WHERE "IsActive" = TRUE
                )
                UPDATE "RETAILER_SALES_ASSIGNMENT" AS assignment
                SET "IsActive" = FALSE,
                    "UnassignedAt" = COALESCE(assignment."UnassignedAt", CURRENT_TIMESTAMP)
                FROM ranked
                WHERE assignment."Id" = ranked."Id" AND ranked.row_number > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_MEMBER_SalesGroupId_AssignedAt",
                table: "SALES_GROUP_MEMBER",
                columns: new[] { "SalesGroupId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_MEMBER_UserAccountId",
                table: "SALES_GROUP_MEMBER",
                column: "UserAccountId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_RetailerId",
                table: "RETAILER_SALES_ASSIGNMENT",
                column: "RetailerId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_SalesGroupId_AssignedAt",
                table: "RETAILER_SALES_ASSIGNMENT",
                columns: new[] { "SalesGroupId", "AssignedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_GROUP_MEMBER_SalesGroupId_AssignedAt",
                table: "SALES_GROUP_MEMBER");

            migrationBuilder.DropIndex(
                name: "IX_SALES_GROUP_MEMBER_UserAccountId",
                table: "SALES_GROUP_MEMBER");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_RetailerId",
                table: "RETAILER_SALES_ASSIGNMENT");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_SalesGroupId_AssignedAt",
                table: "RETAILER_SALES_ASSIGNMENT");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_MEMBER_SalesGroupId",
                table: "SALES_GROUP_MEMBER",
                column: "SalesGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_GROUP_MEMBER_UserAccountId",
                table: "SALES_GROUP_MEMBER",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_RetailerId",
                table: "RETAILER_SALES_ASSIGNMENT",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_SALES_ASSIGNMENT_SalesGroupId",
                table: "RETAILER_SALES_ASSIGNMENT",
                column: "SalesGroupId");
        }
    }
}
