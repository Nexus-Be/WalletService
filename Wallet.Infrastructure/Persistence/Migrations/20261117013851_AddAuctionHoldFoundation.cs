using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionHoldFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferenceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionHolds", x => x.Id);
                    table.CheckConstraint("CK_AuctionHolds_MetadataObject", "jsonb_typeof(\"MetadataJson\") = 'object'");
                    table.CheckConstraint("CK_AuctionHolds_Status", "\"Status\" IN (0, 1, 2, 3)");
                    table.CheckConstraint("CK_AuctionHolds_WholeAmount", "\"Amount\" > 0 AND \"Amount\" = trunc(\"Amount\")");
                    table.ForeignKey(
                        name: "FK_AuctionHolds_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Wallets_LockedBalance",
                table: "Wallets",
                sql: "\"LockedBalance\" >= 0 AND \"LockedBalance\" <= \"Balance\"");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionHolds_OwnerUserId_CreatedAt",
                table: "AuctionHolds",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionHolds_ReferenceType_ReferenceId",
                table: "AuctionHolds",
                columns: new[] { "ReferenceType", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionHolds_WalletId",
                table: "AuctionHolds",
                column: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionHolds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Wallets_LockedBalance",
                table: "Wallets");
        }
    }
}
