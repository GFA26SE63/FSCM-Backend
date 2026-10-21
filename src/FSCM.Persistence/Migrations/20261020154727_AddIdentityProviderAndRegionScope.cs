using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityProviderAndRegionScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_USER_ACCOUNT_EntraObjectId",
                table: "USER_ACCOUNT");

            migrationBuilder.AlterColumn<string>(
                name: "EntraObjectId",
                table: "USER_ACCOUNT",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AddColumn<string>(
                name: "IdentityProvider",
                table: "USER_ACCOUNT",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "USER_ACCOUNT"
                SET "IdentityProvider" = CASE
                    WHEN "EntraObjectId" LIKE 'local:%' THEN 'Local'
                    ELSE 'Entra'
                END;

                UPDATE "USER_ACCOUNT"
                SET "EntraObjectId" = NULL
                WHERE "EntraObjectId" LIKE 'local:%';
                """);

            migrationBuilder.CreateTable(
                name: "USER_REGION_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnassignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_REGION_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_USER_REGION_ASSIGNMENT_REGION_RegionId",
                        column: x => x.RegionId,
                        principalTable: "REGION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_USER_REGION_ASSIGNMENT_USER_ACCOUNT_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_EntraObjectId",
                table: "USER_ACCOUNT",
                column: "EntraObjectId",
                unique: true,
                filter: "\"EntraObjectId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_USER_REGION_ASSIGNMENT_RegionId_IsActive",
                table: "USER_REGION_ASSIGNMENT",
                columns: new[] { "RegionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_USER_REGION_ASSIGNMENT_UserAccountId_RegionId",
                table: "USER_REGION_ASSIGNMENT",
                columns: new[] { "UserAccountId", "RegionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USER_REGION_ASSIGNMENT");

            migrationBuilder.DropIndex(
                name: "IX_USER_ACCOUNT_EntraObjectId",
                table: "USER_ACCOUNT");

            migrationBuilder.Sql(
                """
                UPDATE "USER_ACCOUNT"
                SET "EntraObjectId" = 'local:' || replace("Id"::text, '-', '')
                WHERE "EntraObjectId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "IdentityProvider",
                table: "USER_ACCOUNT");

            migrationBuilder.AlterColumn<string>(
                name: "EntraObjectId",
                table: "USER_ACCOUNT",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_EntraObjectId",
                table: "USER_ACCOUNT",
                column: "EntraObjectId",
                unique: true);
        }
    }
}
