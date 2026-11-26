using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceDynamicRoleLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_USER_ACCOUNT_RoleLinked",
                table: "USER_ACCOUNT",
                sql: "\"RoleDefinitionId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SYS_ROLE_BaseRole",
                table: "SYS_ROLE",
                sql: "\"BaseRole\" IN ('Admin','Operator','WarehouseKeeper','SalesManager','Sales','Retailer') AND ((\"Code\" = 'Admin') = (\"BaseRole\" = 'Admin'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_USER_ACCOUNT_RoleLinked",
                table: "USER_ACCOUNT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SYS_ROLE_BaseRole",
                table: "SYS_ROLE");
        }
    }
}
