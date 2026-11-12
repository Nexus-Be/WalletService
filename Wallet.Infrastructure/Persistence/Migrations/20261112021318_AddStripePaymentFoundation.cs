using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStripePaymentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StripePaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletTopupId = table.Column<Guid>(type: "uuid", nullable: true),
                    DirectPaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderIdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ClientIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CheckoutSessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PaymentIntentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SessionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BlocksReplacement = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripePaymentAttempts", x => x.Id);
                    table.CheckConstraint("CK_StripePaymentAttempts_Purpose", "(\"Purpose\" = 0 AND \"WalletTopupId\" IS NOT NULL AND \"DirectPaymentOrderId\" IS NULL) OR (\"Purpose\" = 1 AND \"WalletTopupId\" IS NULL AND \"DirectPaymentOrderId\" IS NOT NULL)");
                    table.CheckConstraint("CK_StripePaymentAttempts_WholeAmount", "\"Amount\" > 0 AND \"Amount\" = trunc(\"Amount\")");
                    table.ForeignKey(
                        name: "FK_StripePaymentAttempts_DirectPaymentOrders_DirectPaymentOrde~",
                        column: x => x.DirectPaymentOrderId,
                        principalTable: "DirectPaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripePaymentAttempts_WalletTopups_WalletTopupId",
                        column: x => x.WalletTopupId,
                        principalTable: "WalletTopups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StripeWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWebhookEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StripeWebhookEvents_StripePaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "StripePaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentAttempts_CheckoutSessionId",
                table: "StripePaymentAttempts",
                column: "CheckoutSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentAttempts_DirectPaymentOrderId",
                table: "StripePaymentAttempts",
                column: "DirectPaymentOrderId",
                unique: true,
                filter: "\"BlocksReplacement\" = TRUE AND \"DirectPaymentOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentAttempts_OwnerId_ClientIdempotencyKey",
                table: "StripePaymentAttempts",
                columns: new[] { "OwnerId", "ClientIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentAttempts_PaymentIntentId",
                table: "StripePaymentAttempts",
                column: "PaymentIntentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentAttempts_ProviderIdempotencyKey",
                table: "StripePaymentAttempts",
                column: "ProviderIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripePaymentAttempts_WalletTopupId",
                table: "StripePaymentAttempts",
                column: "WalletTopupId",
                unique: true,
                filter: "\"BlocksReplacement\" = TRUE AND \"WalletTopupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_PaymentAttemptId",
                table: "StripeWebhookEvents",
                column: "PaymentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_ProviderEventId",
                table: "StripeWebhookEvents",
                column: "ProviderEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Rolling back Stripe payment data requires an explicitly approved data migration.");
        }
    }
}
