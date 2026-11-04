using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261103090000_AddCancelledPaymentStatus")]
public sealed class AddCancelledPaymentStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_PAYMENT_Status",
            table: "PAYMENT");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PAYMENT_Status",
            table: "PAYMENT",
            sql: "\"Status\" BETWEEN 0 AND 4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_PAYMENT_Status",
            table: "PAYMENT");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PAYMENT_Status",
            table: "PAYMENT",
            sql: "\"Status\" BETWEEN 0 AND 3");
    }
}
