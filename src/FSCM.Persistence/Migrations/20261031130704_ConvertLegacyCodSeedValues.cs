using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertLegacyCodSeedValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"SALES_ORDER\" SET \"PaymentMethod\" = 'Credit' WHERE \"PaymentMethod\" = 'COD';");
            migrationBuilder.Sql("UPDATE \"PAYMENT\" SET \"Method\" = 'Credit' WHERE \"Method\" = 'COD';");
            migrationBuilder.Sql("UPDATE \"MANUAL_PAYMENT\" SET \"Method\" = 'Credit' WHERE \"Method\" = 'COD';");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
