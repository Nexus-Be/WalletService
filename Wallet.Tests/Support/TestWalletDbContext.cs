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
    public DbSet<AuctionHold> AuctionHolds => _inner.AuctionHolds;
    public DbSet<AuctionHoldOperation> AuctionHoldOperations => _inner.AuctionHoldOperations;
    public DbSet<AuctionHoldCapture> AuctionHoldCaptures => _inner.AuctionHoldCaptures;
    public DbSet<AuctionCaptureTransfer> AuctionCaptureTransfers => _inner.AuctionCaptureTransfers;
    public DbSet<AuctionDepositEscrow> AuctionDepositEscrows => _inner.AuctionDepositEscrows;
    public DbSet<OrderEscrow> OrderEscrows => _inner.OrderEscrows;
    public DbSet<WalletTransaction> WalletTransactions => _inner.WalletTransactions;
    public DbSet<BankAccount> BankAccounts => _inner.BankAccounts;
    public DbSet<WalletTopup> WalletTopups => _inner.WalletTopups;
    public DbSet<DirectPaymentOrder> DirectPaymentOrders => _inner.DirectPaymentOrders;
    public DbSet<StripePaymentAttempt> StripePaymentAttempts => _inner.StripePaymentAttempts;
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => _inner.StripeWebhookEvents;
    public DbSet<VnpayIpnLog> VnpayIpnLogs => _inner.VnpayIpnLogs;
    public DbSet<WithdrawalRequest> WithdrawalRequests => _inner.WithdrawalRequests;
    public DbSet<AuditLog> AuditLogs => _inner.AuditLogs;
    public DbSet<LedgerSettings> LedgerSettings => _inner.LedgerSettings;
    public DbSet<LedgerPostingPending> LedgerPostingPending => _inner.LedgerPostingPending;
    public DbSet<LedgerAccount> LedgerAccounts => _inner.LedgerAccounts;
    public DbSet<JournalEntry> JournalEntries => _inner.JournalEntries;
    public DbSet<JournalLine> JournalLines => _inner.JournalLines;
    public DbSet<ReconciliationRun> ReconciliationRuns => _inner.ReconciliationRuns;
    public DbSet<ReconciliationFinding> ReconciliationFindings => _inner.ReconciliationFindings;
    public DbSet<OrderSettlement> OrderSettlements => _inner.OrderSettlements;
    public DbSet<PlatformSubsidyBudget> PlatformSubsidyBudgets => _inner.PlatformSubsidyBudgets;
    public DbSet<PlatformSubsidyReservation> PlatformSubsidyReservations => _inner.PlatformSubsidyReservations;
    public DbSet<RefundFundPayout> RefundFundPayouts => _inner.RefundFundPayouts;
    public DatabaseFacade Database => _inner.Database;

    public Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID must not be empty.", nameof(orderId));
        return Task.CompletedTask;
    }

    public Task LockAuctionKeyAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Auction lock key must not be empty.", nameof(key));
        return Task.CompletedTask;
    }

    public Task<AuctionHold?> LockAuctionHoldAsync(Guid holdId, CancellationToken cancellationToken) =>
        _inner.AuctionHolds.SingleOrDefaultAsync(hold => hold.Id == holdId, cancellationToken);

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

    public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken)
        => _inner.WithdrawalRequests.SingleOrDefaultAsync(r => r.Id == withdrawalId, cancellationToken);

    public Task<OrderEscrow?> LockOrderEscrowByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
        => _inner.OrderEscrows.SingleOrDefaultAsync(e => e.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<AuctionDepositEscrow>> LockDepositEscrowsByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
        => (await _inner.AuctionDepositEscrows.Where(e => e.OrderId == orderId).ToListAsync(cancellationToken))
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList();

    public Task<AuctionDepositEscrow?> LockDepositEscrowByHoldIdAsync(Guid holdId, CancellationToken cancellationToken)
        => _inner.AuctionDepositEscrows.SingleOrDefaultAsync(e => e.HoldId == holdId, cancellationToken);

    public Task<PlatformSubsidyBudget?> LockSubsidyBudgetAsync(Guid budgetId, CancellationToken cancellationToken)
        => _inner.PlatformSubsidyBudgets.SingleOrDefaultAsync(b => b.Id == budgetId, cancellationToken);

    public void EnsureNoPendingChanges() => _inner.EnsureNoPendingChanges();

    public Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
        => _inner.ReloadUnmodifiedAsync(entity, cancellationToken);

    public int SaveChanges() => _inner.SaveChanges();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _inner.SaveChangesAsync(cancellationToken);

    public void Dispose() => _inner.Dispose();
}
