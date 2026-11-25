namespace Wallet.Application.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Wallet.Domain.Entities;

public interface IWalletDbContext
{
    DbSet<WalletAccount> Wallets { get; }
    DbSet<AuctionHold> AuctionHolds { get; }
    DbSet<AuctionHoldOperation> AuctionHoldOperations { get; }
    DbSet<AuctionHoldCapture> AuctionHoldCaptures { get; }
    DbSet<AuctionCaptureTransfer> AuctionCaptureTransfers { get; }
    DbSet<AuctionDepositEscrow> AuctionDepositEscrows { get; }
    DbSet<OrderEscrow> OrderEscrows { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<WalletTopup> WalletTopups { get; }
    DbSet<DirectPaymentOrder> DirectPaymentOrders { get; }
    DbSet<StripePaymentAttempt> StripePaymentAttempts { get; }
    DbSet<StripeWebhookEvent> StripeWebhookEvents { get; }
    DbSet<VnpayIpnLog> VnpayIpnLogs { get; }
    DbSet<WithdrawalRequest> WithdrawalRequests { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<LedgerSettings> LedgerSettings { get; }
    DbSet<LedgerPostingPending> LedgerPostingPending { get; }
    DbSet<LedgerAccount> LedgerAccounts { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalLine> JournalLines { get; }
    DbSet<ReconciliationRun> ReconciliationRuns { get; }
    DbSet<ReconciliationFinding> ReconciliationFindings { get; }
    DbSet<OrderSettlement> OrderSettlements { get; }
    DbSet<PlatformSubsidyBudget> PlatformSubsidyBudgets { get; }
    DbSet<PlatformSubsidyReservation> PlatformSubsidyReservations { get; }
    DbSet<RefundFundPayout> RefundFundPayouts { get; }
    DatabaseFacade Database { get; }

    Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken);
    Task LockAuctionKeyAsync(string key, CancellationToken cancellationToken);
    Task<AuctionHold?> LockAuctionHoldAsync(Guid holdId, CancellationToken cancellationToken);
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
    Task<OrderEscrow?> LockOrderEscrowByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
    /// <summary>Locks every deposit escrow of an order (a Buy Now order has two: the deposit and the goods hold), oldest first.</summary>
    Task<IReadOnlyList<AuctionDepositEscrow>> LockDepositEscrowsByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
    Task<AuctionDepositEscrow?> LockDepositEscrowByHoldIdAsync(Guid holdId, CancellationToken cancellationToken);
    Task<PlatformSubsidyBudget?> LockSubsidyBudgetAsync(Guid budgetId, CancellationToken cancellationToken);
    void EnsureNoPendingChanges();
    Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
