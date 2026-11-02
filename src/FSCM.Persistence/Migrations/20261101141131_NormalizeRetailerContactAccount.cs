using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeRetailerContactAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "RETAILER" AS retailer
                SET "UserAccountId" = account."Id"
                FROM "USER_ACCOUNT" AS account
                WHERE retailer."UserAccountId" IS NULL
                  AND retailer."Email" IS NOT NULL
                  AND lower(account."Email") = lower(retailer."Email");

                INSERT INTO "USER_ACCOUNT"
                    ("Id", "GoogleSubject", "IdentityProvider", "Email", "Phone", "FullName", "Role", "IsActive", "CreatedAt", "UpdatedAt")
                SELECT
                    md5('retailer-contact:' || retailer."Id"::text)::uuid,
                    NULL,
                    'Local',
                    COALESCE(NULLIF(lower(retailer."Email"), ''), lower(retailer."Code") || '@pending.fscm.local'),
                    NULLIF(left(regexp_replace(COALESCE(retailer."Phone", ''), '[^0-9]', '', 'g'), 20), ''),
                    left(COALESCE(NULLIF(retailer."ContactName", ''), retailer."Name"), 150),
                    'Retailer',
                    FALSE,
                    retailer."CreatedAt",
                    retailer."UpdatedAt"
                FROM "RETAILER" AS retailer
                WHERE retailer."UserAccountId" IS NULL;

                UPDATE "RETAILER" AS retailer
                SET "UserAccountId" = md5('retailer-contact:' || retailer."Id"::text)::uuid
                WHERE retailer."UserAccountId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "RETAILER");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "RETAILER",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "RETAILER",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "RETAILER",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "RETAILER" AS retailer
                SET "ContactName" = account."FullName",
                    "Phone" = account."Phone",
                    "Email" = account."Email"
                FROM "USER_ACCOUNT" AS account
                WHERE retailer."UserAccountId" = account."Id";
                """);
        }
    }
}
