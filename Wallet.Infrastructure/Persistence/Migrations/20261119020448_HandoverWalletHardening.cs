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
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_WithdrawalRequests_WalletId";

                ALTER TABLE "WithdrawalRequests" ADD COLUMN IF NOT EXISTS "BankAccountId" uuid;
                ALTER TABLE "WithdrawalRequests" ADD COLUMN IF NOT EXISTS "ClientIdempotencyKey" character varying(100);
                ALTER TABLE "WithdrawalRequests" ADD COLUMN IF NOT EXISTS "CompletedAt" timestamp with time zone;
                ALTER TABLE "WithdrawalRequests" ADD COLUMN IF NOT EXISTS "RequestFingerprint" character varying(64);
                ALTER TABLE "WithdrawalRequests" ADD COLUMN IF NOT EXISTS "StatusReason" character varying(500);

                ALTER TABLE "WalletTransactions" ADD COLUMN IF NOT EXISTS "ClientIdempotencyKey" character varying(128);
                ALTER TABLE "WalletTransactions" ADD COLUMN IF NOT EXISTS "RequestFingerprint" character varying(64);

                CREATE INDEX IF NOT EXISTS "IX_WithdrawalRequests_BankAccountId"
                    ON "WithdrawalRequests" ("BankAccountId");

                DROP INDEX IF EXISTS "IX_WithdrawalRequests_GatewayReferenceId";
                CREATE UNIQUE INDEX "IX_WithdrawalRequests_GatewayReferenceId"
                    ON "WithdrawalRequests" ("GatewayReferenceId")
                    WHERE "GatewayReferenceId" IS NOT NULL AND "IsDeleted" = FALSE;

                CREATE INDEX IF NOT EXISTS "IX_WithdrawalRequests_Status_CreatedAt"
                    ON "WithdrawalRequests" ("Status", "CreatedAt");

                DROP INDEX IF EXISTS "IX_WithdrawalRequests_WalletId_ClientIdempotencyKey";
                CREATE UNIQUE INDEX "IX_WithdrawalRequests_WalletId_ClientIdempotencyKey"
                    ON "WithdrawalRequests" ("WalletId", "ClientIdempotencyKey")
                    WHERE "ClientIdempotencyKey" IS NOT NULL AND "IsDeleted" = FALSE;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTransactions_ReferenceId_ClientIdempotencyKey"
                    ON "WalletTransactions" ("ReferenceId", "ClientIdempotencyKey")
                    WHERE "ClientIdempotencyKey" IS NOT NULL AND "IsDeleted" = FALSE;

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_Wallets_BalanceLocks') THEN
                        ALTER TABLE "Wallets"
                            ADD CONSTRAINT "CK_Wallets_BalanceLocks"
                            CHECK ("Balance" >= 0 AND "LockedBalance" >= 0 AND "LockedBalance" <= "Balance");
                    END IF;
                END $$;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_BankAccounts_UserId_Default"
                    ON "BankAccounts" ("UserId")
                    WHERE "IsDefault" = TRUE AND "IsDeleted" = FALSE;
                """);
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
