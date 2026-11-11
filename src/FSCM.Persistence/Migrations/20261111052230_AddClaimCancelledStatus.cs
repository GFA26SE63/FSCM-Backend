using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimCancelledStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_Status",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_Status",
                table: "RECEIPT_DISCREPANCY",
                sql: "\"Status\" BETWEEN 0 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_Status",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_Status",
                table: "RECEIPT_DISCREPANCY",
                sql: "\"Status\" BETWEEN 0 AND 4");
        }
    }
}
