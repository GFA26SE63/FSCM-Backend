using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

/// <summary>Required runtime reference settings; does not create business/demo data.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261128100000_MoveBackgroundJobsToDatabase")]
public sealed class MoveBackgroundJobsToDatabase : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO "SYSTEM_CONFIGURATION"
          ("Id", "ConfigKey", "ConfigValue", "Description", "ValueType", "Version", "IsActive",
           "UpdatedByUserId", "EffectiveFrom", "CreatedAt", "UpdatedAt")
        SELECT gen_random_uuid(), definition.key, definition.value::jsonb, definition.description, 'object',
          COALESCE((SELECT MAX(existing."Version") FROM "SYSTEM_CONFIGURATION" existing
                    WHERE existing."ConfigKey" = definition.key), 0) + 1,
          TRUE, NULL, now(), now(), now()
        FROM (VALUES
          ('jobs.date_tracker',
           '{"enabled":true,"timeZone":"Asia/Ho_Chi_Minh","runAtLocalTime":"01:30"}',
           'Daily batch expiry schedule'),
          ('jobs.inventory_outbox',
           '{"enabled":true,"pollIntervalSeconds":15,"batchSize":50}',
           'Expired batch notification dispatcher'),
          ('jobs.order_deadline',
           '{"enabled":true,"pollIntervalMinutes":5,"prepaidPaymentWindowDays":3}',
           'Quote and prepaid payment deadline sweep'),
          ('jobs.allocation_recovery',
           '{"enabled":true}',
           'Interrupted allocation recovery')
        ) AS definition(key, value, description)
        WHERE NOT EXISTS (SELECT 1 FROM "SYSTEM_CONFIGURATION" active
                          WHERE active."ConfigKey" = definition.key AND active."IsActive");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Background job settings migration is forward-only.");
}
