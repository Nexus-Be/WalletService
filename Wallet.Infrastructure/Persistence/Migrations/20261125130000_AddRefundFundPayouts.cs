using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wallet.Infrastructure.Persistence;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the record of refunds paid from the refund fund after an order was paid out to the seller (one row per sub-order). Only an
    /// addition. Idempotent, so it runs on a fresh database and on one that has it.
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20261125130000_AddRefundFundPayouts")]
    public partial class AddRefundFundPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""RefundFundPayouts"" (
    ""Id"" uuid NOT NULL,
    ""SubOrderId"" uuid NOT NULL,
    ""ParentOrderId"" uuid NOT NULL,
    ""BuyerId"" uuid NOT NULL,
    ""SellerId"" uuid NOT NULL,
    ""Amount"" numeric(18,2) NOT NULL,
    ""SellerRecoveryTarget"" numeric(18,2) NOT NULL,
    ""SellerRecoveredAmount"" numeric(18,2) NOT NULL,
    ""SellerShortfall"" numeric(18,2) NOT NULL,
    ""FirstApproverId"" uuid NOT NULL,
    ""SecondApproverId"" uuid NULL,
    ""BuyerTransactionId"" uuid NOT NULL,
    ""FundTransactionId"" uuid NOT NULL,
    ""RecoveryTransactionId"" uuid NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""IsDeleted"" boolean NOT NULL,
    ""DeletedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_RefundFundPayouts"" PRIMARY KEY (""Id""),
    CONSTRAINT ""CK_RefundFundPayouts_Amounts"" CHECK (""Amount"" > 0 AND ""SellerRecoveryTarget"" >= 0 AND ""SellerRecoveredAmount"" >= 0 AND ""SellerShortfall"" >= 0 AND ""SellerRecoveredAmount"" <= ""SellerRecoveryTarget"" AND ""SellerRecoveredAmount"" + ""SellerShortfall"" = ""SellerRecoveryTarget"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RefundFundPayouts_SubOrderId"" ON ""RefundFundPayouts"" (""SubOrderId"");
CREATE INDEX IF NOT EXISTS ""IX_RefundFundPayouts_CreatedAt"" ON ""RefundFundPayouts"" (""CreatedAt"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""RefundFundPayouts"";");
        }
    }
}
