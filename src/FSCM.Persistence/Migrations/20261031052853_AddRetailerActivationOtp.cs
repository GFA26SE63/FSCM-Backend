using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailerActivationOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RETAILER_ACTIVATION_CHALLENGE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OtpHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SetupTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestedIpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SetupTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER_ACTIVATION_CHALLENGE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RETAILER_ACTIVATION_CHALLENGE_USER_ACCOUNT_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_ACTIVATION_CHALLENGE_RequestedIpAddress_CreatedAt",
                table: "RETAILER_ACTIVATION_CHALLENGE",
                columns: new[] { "RequestedIpAddress", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_ACTIVATION_CHALLENGE_SetupTokenHash",
                table: "RETAILER_ACTIVATION_CHALLENGE",
                column: "SetupTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_ACTIVATION_CHALLENGE_UserAccountId_CreatedAt",
                table: "RETAILER_ACTIVATION_CHALLENGE",
                columns: new[] { "UserAccountId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RETAILER_ACTIVATION_CHALLENGE");
        }
    }
}
