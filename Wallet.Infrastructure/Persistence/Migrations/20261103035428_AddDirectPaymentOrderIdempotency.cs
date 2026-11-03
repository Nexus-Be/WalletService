using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectPaymentOrderIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DirectPaymentOrders_OrderId",
                table: "DirectPaymentOrders");

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "DirectPaymentOrders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "DirectPaymentOrders",
                keyColumn: "Id",
                keyValue: new Guid("99999999-4444-4444-4444-444444444420"),
                column: "RequestFingerprint",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_DirectPaymentOrders_OrderId",
                table: "DirectPaymentOrders",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DirectPaymentOrders_OrderId",
                table: "DirectPaymentOrders");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "DirectPaymentOrders");

            migrationBuilder.CreateIndex(
                name: "IX_DirectPaymentOrders_OrderId",
                table: "DirectPaymentOrders",
                column: "OrderId");
        }
    }
}
