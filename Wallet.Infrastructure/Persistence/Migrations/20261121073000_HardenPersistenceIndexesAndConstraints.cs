using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenPersistenceIndexesAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Wallets" DROP CONSTRAINT IF EXISTS "CK_Wallets_LockedBalance";

                DROP INDEX IF EXISTS "IX_Wallets_UserId";
                CREATE UNIQUE INDEX "IX_Wallets_UserId"
                    ON "Wallets" ("UserId")
                    WHERE "IsDeleted" = FALSE;

                DROP INDEX IF EXISTS "IX_DirectPaymentOrders_OrderId";
                CREATE UNIQUE INDEX "IX_DirectPaymentOrders_OrderId"
                    ON "DirectPaymentOrders" ("OrderId")
                    WHERE "IsDeleted" = FALSE;

                DROP INDEX IF EXISTS "IX_DirectPaymentOrders_VnpTxnRef";
                CREATE UNIQUE INDEX "IX_DirectPaymentOrders_VnpTxnRef"
                    ON "DirectPaymentOrders" ("VnpTxnRef")
                    WHERE "VnpTxnRef" IS NOT NULL AND "IsDeleted" = FALSE;

                DROP INDEX IF EXISTS "IX_WalletTopups_VnpTxnRef";
                CREATE UNIQUE INDEX "IX_WalletTopups_VnpTxnRef"
                    ON "WalletTopups" ("VnpTxnRef")
                    WHERE "VnpTxnRef" IS NOT NULL AND "IsDeleted" = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_WalletTopups_VnpTxnRef";
                CREATE UNIQUE INDEX "IX_WalletTopups_VnpTxnRef"
                    ON "WalletTopups" ("VnpTxnRef");

                DROP INDEX IF EXISTS "IX_DirectPaymentOrders_VnpTxnRef";
                CREATE UNIQUE INDEX "IX_DirectPaymentOrders_VnpTxnRef"
                    ON "DirectPaymentOrders" ("VnpTxnRef");

                DROP INDEX IF EXISTS "IX_DirectPaymentOrders_OrderId";
                CREATE UNIQUE INDEX "IX_DirectPaymentOrders_OrderId"
                    ON "DirectPaymentOrders" ("OrderId");

                DROP INDEX IF EXISTS "IX_Wallets_UserId";
                CREATE UNIQUE INDEX "IX_Wallets_UserId"
                    ON "Wallets" ("UserId");

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_Wallets_LockedBalance') THEN
                        ALTER TABLE "Wallets"
                            ADD CONSTRAINT "CK_Wallets_LockedBalance"
                            CHECK ("LockedBalance" >= 0 AND "LockedBalance" <= "Balance");
                    END IF;
                END $$;
                """);
        }
    }
}
