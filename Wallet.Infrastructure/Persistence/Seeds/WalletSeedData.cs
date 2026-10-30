namespace Wallet.Infrastructure.Persistence.Seeds;

using Microsoft.EntityFrameworkCore;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

public static class WalletSeedData
{
    public static void SeedAll(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var systemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var adminUserId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var staffUserId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
        var sellerUserId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
        var buyerUserId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

        var systemWalletId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var adminWalletId = Guid.Parse("99999999-1111-1111-1111-111111111111");
        var staffWalletId = Guid.Parse("99999999-2222-2222-2222-222222222222");
        var sellerWalletId = Guid.Parse("99999999-3333-3333-3333-333333333333");
        var buyerWalletId = Guid.Parse("99999999-4444-4444-4444-444444444444");

        // 1. Wallets for System and 4 Users
        modelBuilder.Entity<WalletAccount>().HasData(
            new WalletAccount
            {
                Id = systemWalletId,
                UserId = systemUserId,
                Balance = 1000000000m,
                LockedBalance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new WalletAccount
            {
                Id = adminWalletId,
                UserId = adminUserId,
                Balance = 10000000m,
                LockedBalance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new WalletAccount
            {
                Id = staffWalletId,
                UserId = staffUserId,
                Balance = 5000000m,
                LockedBalance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new WalletAccount
            {
                Id = sellerWalletId,
                UserId = sellerUserId,
                Balance = 50000000m,
                LockedBalance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new WalletAccount
            {
                Id = buyerWalletId,
                UserId = buyerUserId,
                Balance = 20000000m,
                LockedBalance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 2. Bank Account for Seller
        modelBuilder.Entity<BankAccount>().HasData(
            new BankAccount
            {
                Id = Guid.Parse("88888888-3333-3333-3333-333333333333"),
                UserId = sellerUserId.ToString(),
                BankName = "Vietcombank",
                AccountNumber = "9988776655",
                AccountHolderName = "NEXUS OFFICIAL STORE",
                IsDefault = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 3. Initial Transaction (Buyer payment for SubOrder)
        modelBuilder.Entity<WalletTransaction>().HasData(
            new WalletTransaction
            {
                Id = Guid.Parse("99999999-4444-4444-4444-444444444401"),
                WalletId = buyerWalletId,
                Type = WalletTransactionType.PurchaseDebit,
                Amount = 850000m,
                BalanceBefore = 20850000m,
                BalanceAfter = 20000000m,
                ReferenceId = Guid.Parse("02000000-0000-0000-0000-000000000001"), // Commerce SubOrder ID
                Status = TransactionStatus.Success,
                UserId = buyerUserId.ToString(),
                Currency = "VND",
                Note = "Thanh toán đơn hàng SUB-20260101-0001",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 4. Wallet Topup
        var topupTxnRef = "VNP-TOPUP-20260101-0001";
        modelBuilder.Entity<WalletTopup>().HasData(
            new WalletTopup
            {
                Id = Guid.Parse("99999999-4444-4444-4444-444444444410"),
                WalletId = buyerWalletId,
                VnpTxnRef = topupTxnRef,
                VnpTransactionNo = "14285701",
                VnpBankCode = "NCB",
                Amount = 10000000m,
                Status = TopupStatus.Success,
                RawResponse = "{\"vnp_ResponseCode\":\"00\",\"vnp_TransactionStatus\":\"00\"}",
                CreatedAt = seedDate.AddDays(-1),
                UpdatedAt = seedDate.AddDays(-1),
                IsDeleted = false
            }
        );

        // 5. Direct Payment Order
        modelBuilder.Entity<DirectPaymentOrder>().HasData(
            new DirectPaymentOrder
            {
                Id = Guid.Parse("99999999-4444-4444-4444-444444444420"),
                OrderId = Guid.Parse("01000000-0000-0000-0000-000000000001"), // Commerce Order ID
                VnpTxnRef = "VNP-PAY-20260101-0001",
                VnpTransactionNo = "14285702",
                Amount = 850000m,
                Status = TopupStatus.Success,
                RawResponse = "{\"vnp_ResponseCode\":\"00\",\"vnp_TransactionStatus\":\"00\"}",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 6. Withdrawal Request
        var withdrawalId = Guid.Parse("99999999-3333-3333-3333-333333333310");
        modelBuilder.Entity<WithdrawalRequest>().HasData(
            new WithdrawalRequest
            {
                Id = withdrawalId,
                WalletId = sellerWalletId,
                Amount = 5000000m,
                Fee = 0m,
                NetAmount = 5000000m,
                BankName = "Vietcombank",
                BankAccountNumber = "9988776655",
                BankAccountHolder = "NEXUS OFFICIAL STORE",
                Status = WithdrawalStatus.Completed,
                ProcessedBy = adminUserId,
                GatewayReferenceId = "VCB-OUT-20260101-001",
                ProcessedAt = seedDate.AddHours(4),
                CreatedAt = seedDate.AddHours(1),
                UpdatedAt = seedDate.AddHours(4),
                IsDeleted = false
            }
        );

        // 7. VNPay IPN Log
        modelBuilder.Entity<VnpayIpnLog>().HasData(
            new VnpayIpnLog
            {
                Id = Guid.Parse("99999999-4444-4444-4444-444444444430"),
                VnpTxnRef = topupTxnRef,
                VnpResponseCode = "00",
                VnpTransactionNo = "14285701",
                ClientIp = "113.160.22.88",
                RawPayload = "{\"vnp_Amount\":1000000000,\"vnp_BankCode\":\"NCB\",\"vnp_ResponseCode\":\"00\"}",
                ProcessingStatus = IpnProcessingStatus.Processed,
                CreatedAt = seedDate.AddDays(-1),
                UpdatedAt = seedDate.AddDays(-1),
                IsDeleted = false
            }
        );

        // 8. Audit Log
        modelBuilder.Entity<AuditLog>().HasData(
            new AuditLog
            {
                Id = Guid.Parse("99999999-1111-1111-1111-111111111130"),
                ServiceName = "Wallet",
                TraceId = "trace-wallet-withdrawal-001",
                UserId = adminUserId.ToString(),
                UserName = "admin@nexus.com",
                Action = AuditAction.WithdrawalComplete,
                EntityName = "WithdrawalRequest",
                EntityId = withdrawalId.ToString(),
                OldValues = "{\"Status\":\"Processing\"}",
                NewValues = "{\"Status\":\"Completed\",\"ProcessedBy\":\"aaaaaaaa-1111-1111-1111-111111111111\"}",
                ChangedColumns = "[\"Status\",\"ProcessedBy\",\"ProcessedAt\"]",
                IpAddress = "127.0.0.1",
                UserAgent = "NexusAdminPortal/1.0",
                CreatedAt = seedDate.AddHours(4),
                IsSynced = true
            }
        );
    }
}
