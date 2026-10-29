using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationClientContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeepLink",
                table: "NOTIFICATION",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedEntityId",
                table: "NOTIFICATION",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEntityType",
                table: "NOTIFICATION",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_UserAccountId_IsRead_CreatedAt",
                table: "NOTIFICATION",
                columns: new[] { "UserAccountId", "IsRead", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATION_UserAccountId_IsRead_CreatedAt",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "DeepLink",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "RelatedEntityId",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "RelatedEntityType",
                table: "NOTIFICATION");
        }
    }
}
