using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderEscrowCancellationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "OrderEscrows",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "OrderEscrows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "OrderEscrows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "OrderEscrows"
                SET "Status" = 1
                WHERE "RemainingAmount" = 0 AND "Status" = 0;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderEscrows_Status",
                table: "OrderEscrows",
                sql: "(\"Status\" = 0 AND \"RemainingAmount\" > 0 AND \"CancelledAt\" IS NULL AND \"CancelReason\" IS NULL) OR (\"Status\" IN (1, 2) AND \"RemainingAmount\" = 0 AND \"CancelledAt\" IS NULL AND \"CancelReason\" IS NULL) OR (\"Status\" = 3 AND \"RemainingAmount\" = 0 AND \"CancelledAt\" IS NOT NULL AND \"CancelReason\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderEscrows_Status",
                table: "OrderEscrows");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "OrderEscrows");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "OrderEscrows");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OrderEscrows");
        }
    }
}
