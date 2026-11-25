using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wallet.Infrastructure.Persistence;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the durable allocation of seller payouts (protocol V2) and the platform subsidy budget with its per-sub-order reservations.
    /// Only additions. Idempotent, so it runs on a fresh database and on one that has some of it.
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20261121150000_AddOrderSettlementAndPlatformSubsidy")]
    public partial class AddOrderSettlementAndPlatformSubsidy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""OrderSettlements"" (
    ""Id"" uuid NOT NULL,
    ""BusinessType"" character varying(30) NOT NULL,
    ""ParentOrderId"" uuid NOT NULL,
    ""SubOrderId"" uuid NOT NULL,
    ""SellerId"" uuid NOT NULL,
    ""SellerWalletId"" uuid NOT NULL,
    ""SellerReceiveAmount"" numeric(18,2) NOT NULL,
    ""PlatformFeeAmount"" numeric(18,2) NOT NULL,
    ""ShippingAmount"" numeric(18,2) NOT NULL,
    ""SubsidyAmount"" numeric(18,2) NOT NULL,
    ""OrderEscrowAmount"" numeric(18,2) NOT NULL,
    ""DepositEscrowAmount"" numeric(18,2) NOT NULL,
    ""PayloadFingerprint"" character varying(64) NOT NULL,
    ""PayloadVersion"" integer NOT NULL,
    ""EscrowReleaseTransactionId"" uuid NULL,
    ""SubsidyTransactionId"" uuid NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""IsDeleted"" boolean NOT NULL,
    ""DeletedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_OrderSettlements"" PRIMARY KEY (""Id""),
    CONSTRAINT ""CK_OrderSettlements_Amounts"" CHECK (""SellerReceiveAmount"" >= 0 AND ""PlatformFeeAmount"" >= 0 AND ""ShippingAmount"" >= 0 AND ""SubsidyAmount"" >= 0 AND ""OrderEscrowAmount"" >= 0 AND ""DepositEscrowAmount"" >= 0),
    CONSTRAINT ""CK_OrderSettlements_Identity"" CHECK (""OrderEscrowAmount"" + ""DepositEscrowAmount"" + ""SubsidyAmount"" = ""SellerReceiveAmount"" + ""PlatformFeeAmount"" + ""ShippingAmount"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OrderSettlements_BusinessType_ParentOrderId_SubOrderId"" ON ""OrderSettlements"" (""BusinessType"", ""ParentOrderId"", ""SubOrderId"");
CREATE INDEX IF NOT EXISTS ""IX_OrderSettlements_SubOrderId"" ON ""OrderSettlements"" (""SubOrderId"");

CREATE TABLE IF NOT EXISTS ""PlatformSubsidyBudgets"" (
    ""Id"" uuid NOT NULL,
    ""Name"" character varying(150) NOT NULL,
    ""TotalLimit"" numeric(18,2) NOT NULL,
    ""DailyLimit"" numeric(18,2) NOT NULL,
    ""PerOrderCap"" numeric(18,2) NOT NULL,
    ""EvidenceRef"" character varying(500) NOT NULL,
    ""Status"" integer NOT NULL,
    ""CreatedByAdminId"" uuid NOT NULL,
    ""ApprovedByAdminId"" uuid NULL,
    ""ApprovedAt"" timestamp with time zone NULL,
    ""ReservedAmount"" numeric(18,2) NOT NULL,
    ""UsedAmount"" numeric(18,2) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""IsDeleted"" boolean NOT NULL,
    ""DeletedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_PlatformSubsidyBudgets"" PRIMARY KEY (""Id""),
    CONSTRAINT ""CK_PlatformSubsidyBudgets_Limits"" CHECK (""TotalLimit"" > 0 AND ""DailyLimit"" > 0 AND ""PerOrderCap"" > 0 AND ""DailyLimit"" <= ""TotalLimit"" AND ""PerOrderCap"" <= ""DailyLimit""),
    CONSTRAINT ""CK_PlatformSubsidyBudgets_Usage"" CHECK (""ReservedAmount"" >= 0 AND ""UsedAmount"" >= 0 AND ""ReservedAmount"" + ""UsedAmount"" <= ""TotalLimit""),
    CONSTRAINT ""CK_PlatformSubsidyBudgets_Approval"" CHECK ((""Status"" = 0 AND ""ApprovedByAdminId"" IS NULL) OR (""Status"" IN (1, 2) AND ""ApprovedByAdminId"" IS NOT NULL AND ""ApprovedByAdminId"" <> ""CreatedByAdminId""))
);

CREATE TABLE IF NOT EXISTS ""PlatformSubsidyReservations"" (
    ""Id"" uuid NOT NULL,
    ""BudgetId"" uuid NOT NULL,
    ""SubOrderId"" uuid NOT NULL,
    ""OrderId"" uuid NOT NULL,
    ""Amount"" numeric(18,2) NOT NULL,
    ""Status"" integer NOT NULL,
    ""ReleasedAt"" timestamp with time zone NULL,
    ""UsedAt"" timestamp with time zone NULL,
    ""ReleaseReason"" character varying(200) NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""IsDeleted"" boolean NOT NULL,
    ""DeletedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_PlatformSubsidyReservations"" PRIMARY KEY (""Id""),
    CONSTRAINT ""CK_PlatformSubsidyReservations_Amount"" CHECK (""Amount"" > 0),
    CONSTRAINT ""FK_PlatformSubsidyReservations_PlatformSubsidyBudgets_BudgetId"" FOREIGN KEY (""BudgetId"") REFERENCES ""PlatformSubsidyBudgets"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PlatformSubsidyReservations_SubOrderId"" ON ""PlatformSubsidyReservations"" (""SubOrderId"");
CREATE INDEX IF NOT EXISTS ""IX_PlatformSubsidyReservations_BudgetId_CreatedAt"" ON ""PlatformSubsidyReservations"" (""BudgetId"", ""CreatedAt"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS ""PlatformSubsidyReservations"";
DROP TABLE IF EXISTS ""PlatformSubsidyBudgets"";
DROP TABLE IF EXISTS ""OrderSettlements"";
");
        }
    }
}
