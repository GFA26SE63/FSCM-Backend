using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <summary>
    /// Data only: the new screen "Yêu cầu nhận hàng từ nhà máy" (warehouse-factory-requests) used to be part of
    /// "Nhập kho". Every role that could open "Nhập kho" keeps that access by getting the new key once. Later changes
    /// in "Vai trò &amp; phân quyền" are not overridden (the role seeder only fills roles without any permission).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261124020000_GrantFactoryRequestsScreen")]
    public partial class GrantFactoryRequestsScreen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "SYS_ROLE_PERMISSION" ("Id", "RoleId", "PermissionKey", "CreatedAt")
                SELECT gen_random_uuid(), p."RoleId", 'warehouse-factory-requests', now()
                FROM "SYS_ROLE_PERMISSION" p
                WHERE p."PermissionKey" = 'warehouse-inbound'
                  AND NOT EXISTS (
                      SELECT 1 FROM "SYS_ROLE_PERMISSION" x
                      WHERE x."RoleId" = p."RoleId" AND x."PermissionKey" = 'warehouse-factory-requests');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "SYS_ROLE_PERMISSION" WHERE "PermissionKey" = 'warehouse-factory-requests';
                """);
        }
    }
}
