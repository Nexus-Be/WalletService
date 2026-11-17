using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionCaptureAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionDepositEscrows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionDepositEscrows", x => x.Id);
                    table.CheckConstraint("CK_AuctionDepositEscrows_Balance", "\"OriginalAmount\" > 0 AND \"OriginalAmount\" = trunc(\"OriginalAmount\") AND \"RemainingAmount\" >= 0 AND \"RemainingAmount\" <= \"OriginalAmount\" AND \"RemainingAmount\" = trunc(\"RemainingAmount\")");
                    table.ForeignKey(
                        name: "FK_AuctionDepositEscrows_AuctionHolds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "AuctionHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuctionHoldCaptures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionHoldCaptures", x => x.Id);
                    table.CheckConstraint("CK_AuctionHoldCaptures_Purpose", "(\"Purpose\" = 'FORFEIT' AND \"OrderId\" IS NULL) OR (\"Purpose\" = 'ORDER_PAYMENT' AND \"OrderId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AuctionHoldCaptures_AuctionHolds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "AuctionHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuctionCaptureTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WalletTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionCaptureTransfers", x => x.Id);
                    table.CheckConstraint("CK_AuctionCaptureTransfers_Amount", "\"Amount\" > 0 AND \"Amount\" = trunc(\"Amount\")");
                    table.CheckConstraint("CK_AuctionCaptureTransfers_Recipient", "(\"RecipientType\" = 'SELLER_WALLET' AND \"RecipientUserId\" IS NOT NULL AND \"WalletTransactionId\" IS NOT NULL) OR (\"RecipientType\" IN ('PLATFORM', 'SYSTEM_ESCROW') AND \"RecipientUserId\" IS NULL AND \"WalletTransactionId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_AuctionCaptureTransfers_AuctionHoldCaptures_CaptureId",
                        column: x => x.CaptureId,
                        principalTable: "AuctionHoldCaptures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuctionCaptureTransfers_WalletTransactions_WalletTransactio~",
                        column: x => x.WalletTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionCaptureTransfers_CaptureId",
                table: "AuctionCaptureTransfers",
                column: "CaptureId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionCaptureTransfers_WalletTransactionId",
                table: "AuctionCaptureTransfers",
                column: "WalletTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionDepositEscrows_HoldId",
                table: "AuctionDepositEscrows",
                column: "HoldId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionDepositEscrows_OrderId",
                table: "AuctionDepositEscrows",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionHoldCaptures_HoldId",
                table: "AuctionHoldCaptures",
                column: "HoldId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionCaptureTransfers");

            migrationBuilder.DropTable(
                name: "AuctionDepositEscrows");

            migrationBuilder.DropTable(
                name: "AuctionHoldCaptures");
        }
    }
}
