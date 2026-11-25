using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wallet.Infrastructure.Persistence;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the shadow ledger (settings, posting queue, accounts, journal) and the reconciliation tables.
    /// Only additions: no existing column or row changes meaning. Idempotent, so it runs on a fresh database and on one that has some of it.
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20261121120000_AddShadowLedgerAndReconciliation")]
    public partial class AddShadowLedgerAndReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Wallets"" ADD COLUMN IF NOT EXISTS ""LedgerEpoch"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""Wallets"" ADD COLUMN IF NOT EXISTS ""LedgerSeq"" bigint NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS ""LedgerSettings"" (
    ""Id"" integer NOT NULL,
    ""Enabled"" boolean NOT NULL DEFAULT false,
    ""ShadowEpoch"" integer NOT NULL DEFAULT 0,
    ""Version"" integer NOT NULL DEFAULT 0,
    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ""UpdatedBy"" uuid NULL,
    CONSTRAINT ""PK_LedgerSettings"" PRIMARY KEY (""Id"")
);
INSERT INTO ""LedgerSettings"" (""Id"", ""Enabled"", ""ShadowEpoch"", ""Version"") VALUES (1, false, 0, 0) ON CONFLICT (""Id"") DO NOTHING;

CREATE TABLE IF NOT EXISTS ""LedgerPostingPending"" (
    ""Id"" uuid NOT NULL,
    ""WalletId"" uuid NOT NULL,
    ""LedgerEpoch"" integer NOT NULL,
    ""LedgerSeq"" bigint NOT NULL,
    ""IsEpochOpening"" boolean NOT NULL DEFAULT false,
    ""WalletTransactionId"" uuid NULL,
    ""RelatedTransactionIds"" character varying(2000) NULL,
    ""Type"" character varying(60) NOT NULL,
    ""Delta"" numeric(18,2) NOT NULL,
    ""WalletBalanceAfter"" numeric(18,2) NOT NULL,
    ""OpeningBalance"" numeric(18,2) NOT NULL DEFAULT 0,
    ""Status"" integer NOT NULL DEFAULT 0,
    ""Attempts"" integer NOT NULL DEFAULT 0,
    ""LastError"" character varying(1000) NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ""PostedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_LedgerPostingPending"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LedgerPostingPending_WalletId_LedgerEpoch_LedgerSeq"" ON ""LedgerPostingPending"" (""WalletId"", ""LedgerEpoch"", ""LedgerSeq"");
CREATE INDEX IF NOT EXISTS ""IX_LedgerPostingPending_Status_CreatedAt"" ON ""LedgerPostingPending"" (""Status"", ""CreatedAt"");

CREATE TABLE IF NOT EXISTS ""LedgerAccounts"" (
    ""Code"" character varying(60) NOT NULL,
    ""Name"" character varying(150) NOT NULL,
    CONSTRAINT ""PK_LedgerAccounts"" PRIMARY KEY (""Code"")
);

CREATE TABLE IF NOT EXISTS ""JournalEntries"" (
    ""Id"" uuid NOT NULL,
    ""WalletId"" uuid NOT NULL,
    ""PendingId"" uuid NULL,
    ""WalletTransactionId"" uuid NULL,
    ""Source"" character varying(20) NOT NULL,
    ""Description"" character varying(300) NOT NULL DEFAULT '',
    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT ""PK_JournalEntries"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_JournalEntries_WalletId"" ON ""JournalEntries"" (""WalletId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_JournalEntries_PendingId"" ON ""JournalEntries"" (""PendingId"") WHERE ""PendingId"" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ""IX_JournalEntries_WalletTransactionId"" ON ""JournalEntries"" (""WalletTransactionId"");

CREATE TABLE IF NOT EXISTS ""JournalLines"" (
    ""Id"" uuid NOT NULL,
    ""EntryId"" uuid NOT NULL,
    ""AccountCode"" character varying(60) NOT NULL,
    ""Debit"" numeric(18,2) NOT NULL DEFAULT 0,
    ""Credit"" numeric(18,2) NOT NULL DEFAULT 0,
    ""WalletId"" uuid NULL,
    CONSTRAINT ""PK_JournalLines"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_JournalLines_JournalEntries_EntryId"" FOREIGN KEY (""EntryId"") REFERENCES ""JournalEntries"" (""Id"") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ""IX_JournalLines_EntryId"" ON ""JournalLines"" (""EntryId"");
CREATE INDEX IF NOT EXISTS ""IX_JournalLines_AccountCode"" ON ""JournalLines"" (""AccountCode"");

CREATE TABLE IF NOT EXISTS ""ReconciliationRuns"" (
    ""Id"" uuid NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ""CreatedBy"" uuid NULL,
    ""Status"" integer NOT NULL DEFAULT 0,
    ""FromDate"" timestamp with time zone NULL,
    ""ToDate"" timestamp with time zone NULL,
    ""Invariants"" character varying(200) NULL,
    ""SkippedNote"" character varying(500) NULL,
    ""UnavailableSources"" character varying(300) NULL,
    ""FindingCount"" integer NOT NULL DEFAULT 0,
    CONSTRAINT ""PK_ReconciliationRuns"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_ReconciliationRuns_CreatedAt"" ON ""ReconciliationRuns"" (""CreatedAt"");

CREATE TABLE IF NOT EXISTS ""ReconciliationFindings"" (
    ""Id"" uuid NOT NULL,
    ""RunId"" uuid NOT NULL,
    ""InvariantCode"" character varying(10) NOT NULL,
    ""Code"" character varying(60) NOT NULL,
    ""Severity"" character varying(20) NOT NULL,
    ""Status"" integer NOT NULL DEFAULT 0,
    ""ReferenceId"" character varying(100) NULL,
    ""Source"" character varying(60) NULL,
    ""ExpectedValue"" character varying(200) NULL,
    ""ActualValue"" character varying(200) NULL,
    ""Description"" character varying(500) NULL,
    ""Note"" character varying(2000) NULL,
    ""DedupKey"" character varying(200) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ""UpdatedBy"" uuid NULL,
    CONSTRAINT ""PK_ReconciliationFindings"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ReconciliationFindings_DedupKey"" ON ""ReconciliationFindings"" (""DedupKey"");
CREATE INDEX IF NOT EXISTS ""IX_ReconciliationFindings_Status_CreatedAt"" ON ""ReconciliationFindings"" (""Status"", ""CreatedAt"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS ""ReconciliationFindings"";
DROP TABLE IF EXISTS ""ReconciliationRuns"";
DROP TABLE IF EXISTS ""JournalLines"";
DROP TABLE IF EXISTS ""JournalEntries"";
DROP TABLE IF EXISTS ""LedgerAccounts"";
DROP TABLE IF EXISTS ""LedgerPostingPending"";
DROP TABLE IF EXISTS ""LedgerSettings"";
ALTER TABLE ""Wallets"" DROP COLUMN IF EXISTS ""LedgerSeq"";
ALTER TABLE ""Wallets"" DROP COLUMN IF EXISTS ""LedgerEpoch"";");
        }
    }
}
