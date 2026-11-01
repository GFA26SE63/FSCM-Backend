using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceEntraWithGoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EntraObjectId",
                table: "USER_ACCOUNT",
                newName: "GoogleSubject");

            migrationBuilder.RenameIndex(
                name: "IX_USER_ACCOUNT_EntraObjectId",
                table: "USER_ACCOUNT",
                newName: "IX_USER_ACCOUNT_GoogleSubject");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleSubject",
                table: "USER_ACCOUNT",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE "USER_ACCOUNT"
                SET "IdentityProvider" = 'Google'
                WHERE "IdentityProvider" = 'Entra';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "USER_ACCOUNT"
                SET "IdentityProvider" = 'Entra'
                WHERE "IdentityProvider" = 'Google';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "GoogleSubject",
                table: "USER_ACCOUNT",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.RenameIndex(
                name: "IX_USER_ACCOUNT_GoogleSubject",
                table: "USER_ACCOUNT",
                newName: "IX_USER_ACCOUNT_EntraObjectId");

            migrationBuilder.RenameColumn(
                name: "GoogleSubject",
                table: "USER_ACCOUNT",
                newName: "EntraObjectId");
        }
    }
}
