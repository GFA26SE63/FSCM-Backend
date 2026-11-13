using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailerCreditLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "RETAILER",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RETAILER_CreditLimit",
                table: "RETAILER",
                sql: "\"CreditLimit\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RETAILER_CreditLimit",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "RETAILER");
        }
    }
}
