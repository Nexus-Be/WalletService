namespace Wallet.Infrastructure.Persistence.Seeds;

using Microsoft.EntityFrameworkCore;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

public static class WalletSeedData
{
    public static void SeedSystemWallet(ModelBuilder modelBuilder)
    {
        var systemWalletId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var systemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var seedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<WalletAccount>().HasData(
            new WalletAccount
            {
                Id = systemWalletId,
                UserId = systemUserId,
                Balance = 0m,
                LockedBalance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );
    }
}
