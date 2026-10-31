using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TraceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true),
                    ChangedColumns = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsSynced = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BankName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DirectPaymentOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    VnpTxnRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VnpTransactionNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawResponse = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectPaymentOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VnpayIpnLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VnpTxnRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VnpResponseCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VnpTransactionNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientIp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VnpayIpnLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LockedBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTopups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    VnpTxnRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VnpTransactionNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VnpBankCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawResponse = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTopups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTopups_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WithdrawalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BankAccountNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BankAccountHolder = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProcessedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    GatewayReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithdrawalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithdrawalRequests_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AuditLogs",
                columns: new[] { "Id", "Action", "ChangedColumns", "CreatedAt", "EntityId", "EntityName", "IpAddress", "IsSynced", "NewValues", "OldValues", "ServiceName", "TraceId", "UserAgent", "UserId", "UserName" },
                values: new object[] { new Guid("99999999-1111-1111-1111-111111111130"), 7, "[\"Status\",\"ProcessedBy\",\"ProcessedAt\"]", new DateTimeOffset(new DateTime(2026, 1, 1, 4, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "99999999-3333-3333-3333-333333333310", "WithdrawalRequest", "127.0.0.1", true, "{\"Status\":\"Completed\",\"ProcessedBy\":\"aaaaaaaa-1111-1111-1111-111111111111\"}", "{\"Status\":\"Processing\"}", "Wallet", "trace-wallet-withdrawal-001", "NexusAdminPortal/1.0", "aaaaaaaa-1111-1111-1111-111111111111", "admin@nexus.com" });

            migrationBuilder.InsertData(
                table: "BankAccounts",
                columns: new[] { "Id", "AccountHolderName", "AccountNumber", "BankName", "CreatedAt", "DeletedAt", "IsDefault", "IsDeleted", "UpdatedAt", "UserId" },
                values: new object[] { new Guid("88888888-3333-3333-3333-333333333333"), "NEXUS OFFICIAL STORE", "9988776655", "Vietcombank", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "cccccccc-3333-3333-3333-333333333333" });

            migrationBuilder.InsertData(
                table: "DirectPaymentOrders",
                columns: new[] { "Id", "Amount", "CreatedAt", "DeletedAt", "IsDeleted", "OrderId", "RawResponse", "Status", "UpdatedAt", "VnpTransactionNo", "VnpTxnRef" },
                values: new object[] { new Guid("99999999-4444-4444-4444-444444444420"), 850000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, new Guid("01000000-0000-0000-0000-000000000001"), "{\"vnp_ResponseCode\":\"00\",\"vnp_TransactionStatus\":\"00\"}", 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "14285702", "VNP-PAY-20260101-0001" });

            migrationBuilder.InsertData(
                table: "VnpayIpnLogs",
                columns: new[] { "Id", "ClientIp", "CreatedAt", "DeletedAt", "IsDeleted", "ProcessingStatus", "RawPayload", "UpdatedAt", "VnpResponseCode", "VnpTransactionNo", "VnpTxnRef" },
                values: new object[] { new Guid("99999999-4444-4444-4444-444444444430"), "113.160.22.88", new DateTimeOffset(new DateTime(2025, 12, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, 0, "{\"vnp_Amount\":1000000000,\"vnp_BankCode\":\"NCB\",\"vnp_ResponseCode\":\"00\"}", new DateTimeOffset(new DateTime(2025, 12, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "00", "14285701", "VNP-TOPUP-20260101-0001" });

            migrationBuilder.InsertData(
                table: "Wallets",
                columns: new[] { "Id", "Balance", "CreatedAt", "CreatedBy", "Currency", "DeletedAt", "IsDeleted", "LockedBalance", "Status", "UpdatedAt", "UpdatedBy", "UserId" },
                values: new object[,]
                {
                    { new Guid("99999999-1111-1111-1111-111111111111"), 10000000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), 0, null, false, 0m, "ACTIVE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("aaaaaaaa-1111-1111-1111-111111111111") },
                    { new Guid("99999999-2222-2222-2222-222222222222"), 5000000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), 0, null, false, 0m, "ACTIVE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("bbbbbbbb-2222-2222-2222-222222222222") },
                    { new Guid("99999999-3333-3333-3333-333333333333"), 50000000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), 0, null, false, 0m, "ACTIVE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("cccccccc-3333-3333-3333-333333333333") },
                    { new Guid("99999999-4444-4444-4444-444444444444"), 20000000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), 0, null, false, 0m, "ACTIVE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("dddddddd-4444-4444-4444-444444444444") },
                    { new Guid("99999999-9999-9999-9999-999999999999"), 1000000000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), 0, null, false, 0m, "ACTIVE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("00000000-0000-0000-0000-000000000001") }
                });

            migrationBuilder.InsertData(
                table: "WalletTopups",
                columns: new[] { "Id", "Amount", "CreatedAt", "DeletedAt", "IsDeleted", "RawResponse", "Status", "UpdatedAt", "VnpBankCode", "VnpTransactionNo", "VnpTxnRef", "WalletId" },
                values: new object[] { new Guid("99999999-4444-4444-4444-444444444410"), 10000000m, new DateTimeOffset(new DateTime(2025, 12, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, "{\"vnp_ResponseCode\":\"00\",\"vnp_TransactionStatus\":\"00\"}", 1, new DateTimeOffset(new DateTime(2025, 12, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "NCB", "14285701", "VNP-TOPUP-20260101-0001", new Guid("99999999-4444-4444-4444-444444444444") });

            migrationBuilder.InsertData(
                table: "WalletTransactions",
                columns: new[] { "Id", "Amount", "BalanceAfter", "BalanceBefore", "CreatedAt", "Currency", "DeletedAt", "IsDeleted", "Note", "ReferenceId", "Status", "Type", "UpdatedAt", "UserId", "WalletId" },
                values: new object[] { new Guid("99999999-4444-4444-4444-444444444401"), 850000m, 20000000m, 20850000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "VND", null, false, "Thanh toán đơn hàng SUB-20260101-0001", new Guid("02000000-0000-0000-0000-000000000001"), 1, 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "dddddddd-4444-4444-4444-444444444444", new Guid("99999999-4444-4444-4444-444444444444") });

            migrationBuilder.InsertData(
                table: "WithdrawalRequests",
                columns: new[] { "Id", "Amount", "BankAccountHolder", "BankAccountNumber", "BankName", "CreatedAt", "DeletedAt", "Fee", "GatewayReferenceId", "IsDeleted", "NetAmount", "ProcessedAt", "ProcessedBy", "Status", "UpdatedAt", "WalletId" },
                values: new object[] { new Guid("99999999-3333-3333-3333-333333333310"), 5000000m, "NEXUS OFFICIAL STORE", "9988776655", "Vietcombank", new DateTimeOffset(new DateTime(2026, 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 0m, "VCB-OUT-20260101-001", false, 5000000m, new DateTimeOffset(new DateTime(2026, 1, 1, 4, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("aaaaaaaa-1111-1111-1111-111111111111"), 3, new DateTimeOffset(new DateTime(2026, 1, 1, 4, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("99999999-3333-3333-3333-333333333333") });

            migrationBuilder.CreateIndex(
                name: "IX_DirectPaymentOrders_OrderId",
                table: "DirectPaymentOrders",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectPaymentOrders_VnpTxnRef",
                table: "DirectPaymentOrders",
                column: "VnpTxnRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VnpayIpnLogs_VnpTxnRef",
                table: "VnpayIpnLogs",
                column: "VnpTxnRef");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                table: "Wallets",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopups_VnpTxnRef",
                table: "WalletTopups",
                column: "VnpTxnRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopups_WalletId",
                table: "WalletTopups",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_ReferenceId_Type",
                table: "WalletTransactions",
                columns: new[] { "ReferenceId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_UserId_CreatedAt",
                table: "WalletTransactions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId",
                table: "WalletTransactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalRequests_WalletId",
                table: "WithdrawalRequests",
                column: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "DirectPaymentOrders");

            migrationBuilder.DropTable(
                name: "VnpayIpnLogs");

            migrationBuilder.DropTable(
                name: "WalletTopups");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "WithdrawalRequests");

            migrationBuilder.DropTable(
                name: "Wallets");
        }
    }
}
