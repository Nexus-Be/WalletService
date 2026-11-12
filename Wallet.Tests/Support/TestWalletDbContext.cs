namespace Wallet.Tests.Support;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Infrastructure.Persistence;

public sealed class TestWalletDbContext : IWalletDbContext, IDisposable
{
    private readonly WalletDbContext _inner;

    public TestWalletDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _inner = new WalletDbContext(options);
        _inner.Database.EnsureCreated();
    }

    public DbSet<WalletAccount> Wallets => _inner.Wallets;
    public DbSet<WalletTransaction> WalletTransactions => _inner.WalletTransactions;
    public DbSet<BankAccount> BankAccounts => _inner.BankAccounts;
    public DbSet<WalletTopup> WalletTopups => _inner.WalletTopups;
    public DbSet<DirectPaymentOrder> DirectPaymentOrders => _inner.DirectPaymentOrders;
    public DbSet<StripePaymentAttempt> StripePaymentAttempts => _inner.StripePaymentAttempts;
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => _inner.StripeWebhookEvents;
    public DbSet<VnpayIpnLog> VnpayIpnLogs => _inner.VnpayIpnLogs;
    public DbSet<WithdrawalRequest> WithdrawalRequests => _inner.WithdrawalRequests;
    public DbSet<AuditLog> AuditLogs => _inner.AuditLogs;
    public DatabaseFacade Database => _inner.Database;

    public Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID must not be empty.", nameof(orderId));
        return Task.CompletedTask;
    }

    public Task<WalletAccount?> LockWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken)
        => _inner.Wallets.SingleOrDefaultAsync(w => w.UserId == userId, cancellationToken);

    public Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken)
        => _inner.Wallets.SingleOrDefaultAsync(w => w.Id == walletId, cancellationToken);

    public Task<WalletTopup?> LockTopupByIdAsync(Guid topupId, CancellationToken cancellationToken)
        => _inner.WalletTopups.SingleOrDefaultAsync(t => t.Id == topupId, cancellationToken);

    public Task<DirectPaymentOrder?> LockDirectPaymentOrderByIdAsync(Guid orderId, CancellationToken cancellationToken)
        => _inner.DirectPaymentOrders.SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);

    public Task<StripePaymentAttempt?> LockStripePaymentAttemptAsync(Guid attemptId, CancellationToken cancellationToken)
        => _inner.StripePaymentAttempts.SingleOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);

    public Task<WalletTopup?> LockTopupByReferenceAsync(string transactionReference, CancellationToken cancellationToken)
        => _inner.WalletTopups.SingleOrDefaultAsync(t => t.VnpTxnRef == transactionReference, cancellationToken);

    public Task<DirectPaymentOrder?> LockDirectPaymentOrderByReferenceAsync(
        string transactionReference,
        CancellationToken cancellationToken)
        => _inner.DirectPaymentOrders.SingleOrDefaultAsync(
            order => order.VnpTxnRef == transactionReference,
            cancellationToken);

    public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken)
        => _inner.WithdrawalRequests.SingleOrDefaultAsync(r => r.Id == withdrawalId, cancellationToken);

    public void EnsureNoPendingChanges() => _inner.EnsureNoPendingChanges();

    public Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
        => _inner.ReloadUnmodifiedAsync(entity, cancellationToken);

    public int SaveChanges() => _inner.SaveChanges();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _inner.SaveChangesAsync(cancellationToken);

    public void Dispose() => _inner.Dispose();
}
