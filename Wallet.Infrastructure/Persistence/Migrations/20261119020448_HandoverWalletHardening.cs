using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HandoverWalletHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_WalletId",
                table: "WithdrawalRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "WithdrawalRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientIdempotencyKey",
                table: "WithdrawalRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "WithdrawalRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "WithdrawalRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "WithdrawalRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientIdempotencyKey",
                table: "WalletTransactions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "WalletTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_BankAccountId",
                table: "WithdrawalRequests",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_GatewayReferenceId",
                table: "WithdrawalRequests",
                column: "GatewayReferenceId",
                unique: true,
                filter: "\"GatewayReferenceId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_Status_CreatedAt",
                table: "WithdrawalRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_WalletId_ClientIdempotencyKey",
                table: "WithdrawalRequests",
                columns: new[] { "WalletId", "ClientIdempotencyKey" },
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_ReferenceId_ClientIdempotencyKey",
                table: "WalletTransactions",
                columns: new[] { "ReferenceId", "ClientIdempotencyKey" },
                unique: true,
                filter: "\"ClientIdempotencyKey\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Wallets_BalanceLocks",
                table: "Wallets",
                sql: "\"Balance\" >= 0 AND \"LockedBalance\" >= 0 AND \"LockedBalance\" <= \"Balance\"");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_UserId_Default",
                table: "BankAccounts",
                column: "UserId",
                unique: true,
                filter: "\"IsDefault\" = TRUE AND \"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_BankAccountId",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_GatewayReferenceId",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_Status_CreatedAt",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WithdrawalRequests_WalletId_ClientIdempotencyKey",
                table: "WithdrawalRequests");

            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_ReferenceId_ClientIdempotencyKey",
                table: "WalletTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Wallets_BalanceLocks",
                table: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_BankAccounts_UserId_Default",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "ClientIdempotencyKey",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "WithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "ClientIdempotencyKey",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "WalletTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_WalletId",
                table: "WithdrawalRequests",
                column: "WalletId");
        }
    }
}
