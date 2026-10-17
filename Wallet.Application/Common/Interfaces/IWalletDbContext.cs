namespace Wallet.Application.Common.Interfaces;

using Microsoft.EntityFrameworkCore;
using Wallet.Domain.Entities;

public interface IWalletDbContext
{
    DbSet<WalletAccount> Wallets { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }
    DbSet<BankAccount> BankAccounts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
