using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailerDeclarationContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RETAILER_DECLARATION_CONTACT",
                columns: table => new
                {
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER_DECLARATION_CONTACT", x => x.RetailerId);
                    table.ForeignKey(
                        name: "FK_RETAILER_DECLARATION_CONTACT_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DECLARATION_CONTACT_Email",
                table: "RETAILER_DECLARATION_CONTACT",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DECLARATION_CONTACT_Phone",
                table: "RETAILER_DECLARATION_CONTACT",
                column: "Phone",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RETAILER_DECLARATION_CONTACT");
        }
    }
}
