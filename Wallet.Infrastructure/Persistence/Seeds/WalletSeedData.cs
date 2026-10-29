using Microsoft.EntityFrameworkCore;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

namespace Wallet.Infrastructure.Persistence.Seeds;

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
    }
}