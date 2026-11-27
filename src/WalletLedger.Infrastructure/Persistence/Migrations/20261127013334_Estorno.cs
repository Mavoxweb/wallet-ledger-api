using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WalletLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Estorno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reversed_by_id",
                table: "transactions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reversed_by_id",
                table: "transactions");
        }
    }
}
