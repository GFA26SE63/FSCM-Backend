using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueAccountPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "USER_ACCOUNT" IN SHARE ROW EXCLUSIVE MODE;
                UPDATE "USER_ACCOUNT" SET "Phone" = NULLIF(btrim("Phone"), '')
                WHERE "Phone" IS DISTINCT FROM NULLIF(btrim("Phone"), '');

                -- Retire only unused legacy placeholder identities, retaining accounts and audit history.
                WITH candidates AS (
                    SELECT u."Id", u."Phone" FROM "USER_ACCOUNT" u
                    WHERE u."Role" = 'Retailer' AND NOT u."IsActive"
                      AND u."Email" LIKE '%@pending.fscm.local' AND u."Phone" IS NOT NULL
                      AND EXISTS (SELECT 1 FROM "USER_ACCOUNT" owner WHERE owner."Phone" = u."Phone"
                          AND owner."Id" <> u."Id" AND owner."Email" NOT LIKE '%@pending.fscm.local')
                      AND NOT EXISTS (SELECT 1 FROM "RETAILER" r WHERE r."UserAccountId" = u."Id")
                      AND NOT EXISTS (SELECT 1 FROM "USER_CREDENTIAL" c WHERE c."UserAccountId" = u."Id")
                      AND NOT EXISTS (SELECT 1 FROM "REFRESH_TOKEN" t WHERE t."UserAccountId" = u."Id")
                    FOR UPDATE
                ), released AS (
                    UPDATE "USER_ACCOUNT" u SET "Phone" = NULL, "UpdatedAt" = now()
                    FROM candidates c WHERE u."Id" = c."Id" RETURNING u."Id", c."Phone"
                )
                INSERT INTO "AUDIT_LOG" ("Id", "UserAccountId", "EntityName", "EntityId", "Action", "OldValues", "NewValues", "Reason", "CreatedAt")
                SELECT gen_random_uuid(), NULL, 'UserAccount', "Id", 'LegacyPlaceholderPhoneReleased',
                    jsonb_build_object('Phone', "Phone"), jsonb_build_object('Phone', NULL),
                    'Released an unused legacy placeholder phone before enforcing account uniqueness.', now()
                FROM released;

                DO $$ BEGIN
                    IF EXISTS (SELECT "Phone" FROM "USER_ACCOUNT" WHERE "Phone" IS NOT NULL
                        GROUP BY "Phone" HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Account phone duplicates remain. Resolve ownership before applying AddUniqueAccountPhone; no accounts have been deleted.';
                    END IF;
                END $$;
                """);
            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_Phone",
                table: "USER_ACCOUNT",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_USER_ACCOUNT_PhoneCanonical",
                table: "USER_ACCOUNT",
                sql: "\"Phone\" IS NULL OR (\"Phone\" = btrim(\"Phone\") AND \"Phone\" <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_USER_ACCOUNT_Phone",
                table: "USER_ACCOUNT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_USER_ACCOUNT_PhoneCanonical",
                table: "USER_ACCOUNT");
        }
    }
}
