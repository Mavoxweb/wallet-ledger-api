using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WalletLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChavesPix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pix_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    value = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pix_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_pix_keys_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pix_keys_account_id",
                table: "pix_keys",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_pix_keys_value",
                table: "pix_keys",
                column: "value",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pix_keys");
        }
    }
}
