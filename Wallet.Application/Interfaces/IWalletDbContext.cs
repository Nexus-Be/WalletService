namespace Wallet.Application.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wallet.Domain.Entities;

public interface IWalletDbContext
{
    DbSet<WalletAccount> Wallets { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<WalletTopup> WalletTopups { get; }
    DbSet<DirectPaymentOrder> DirectPaymentOrders { get; }
    DbSet<StripePaymentAttempt> StripePaymentAttempts { get; }
    DbSet<StripeWebhookEvent> StripeWebhookEvents { get; }
    DbSet<VnpayIpnLog> VnpayIpnLogs { get; }
    DbSet<WithdrawalRequest> WithdrawalRequests { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DatabaseFacade Database { get; }

    Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken);
    Task<WalletAccount?> LockWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken);
    Task<WalletTopup?> LockTopupByIdAsync(Guid topupId, CancellationToken cancellationToken);
    Task<DirectPaymentOrder?> LockDirectPaymentOrderByIdAsync(Guid orderId, CancellationToken cancellationToken);
    Task<StripePaymentAttempt?> LockStripePaymentAttemptAsync(Guid attemptId, CancellationToken cancellationToken);
    Task<WalletTopup?> LockTopupByReferenceAsync(string transactionReference, CancellationToken cancellationToken);
    Task<DirectPaymentOrder?> LockDirectPaymentOrderByReferenceAsync(
        string transactionReference,
        CancellationToken cancellationToken);
    Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken);
    void EnsureNoPendingChanges();
    Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
