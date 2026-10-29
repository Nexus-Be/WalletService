using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wallet.Domain.Entities;

namespace Wallet.Application.Common.Interfaces;

public interface IWalletDbContext
{
    DbSet<WalletAccount> Wallets { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<WalletTopup> WalletTopups { get; }
    DbSet<VnpayIpnLog> VnpayIpnLogs { get; }
    DbSet<WithdrawalRequest> WithdrawalRequests { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DatabaseFacade Database { get; }

    Task<WalletAccount?> LockWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken);
    Task<WalletTopup?> LockTopupByReferenceAsync(string transactionReference, CancellationToken cancellationToken);
    Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}