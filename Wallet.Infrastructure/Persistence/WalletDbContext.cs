namespace Wallet.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;

public class WalletDbContext : DbContext, IWalletDbContext
{
    public WalletDbContext(DbContextOptions<WalletDbContext> options) : base(options)
    {
    }

    public DbSet<WalletAccount> Wallets => Set<WalletAccount>();
    public DbSet<AuctionHold> AuctionHolds => Set<AuctionHold>();
    public DbSet<AuctionHoldOperation> AuctionHoldOperations => Set<AuctionHoldOperation>();
    public DbSet<AuctionHoldCapture> AuctionHoldCaptures => Set<AuctionHoldCapture>();
    public DbSet<AuctionCaptureTransfer> AuctionCaptureTransfers => Set<AuctionCaptureTransfer>();
    public DbSet<AuctionDepositEscrow> AuctionDepositEscrows => Set<AuctionDepositEscrow>();
    public DbSet<OrderEscrow> OrderEscrows => Set<OrderEscrow>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<WalletTopup> WalletTopups => Set<WalletTopup>();
    public DbSet<DirectPaymentOrder> DirectPaymentOrders => Set<DirectPaymentOrder>();
    public DbSet<StripePaymentAttempt> StripePaymentAttempts => Set<StripePaymentAttempt>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();
    public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();
    public DbSet<VnpayIpnLog> VnpayIpnLogs => Set<VnpayIpnLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LedgerSettings> LedgerSettings => Set<LedgerSettings>();
    public DbSet<LedgerPostingPending> LedgerPostingPending => Set<LedgerPostingPending>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();
    public DbSet<ReconciliationFinding> ReconciliationFindings => Set<ReconciliationFinding>();
    public DbSet<OrderSettlement> OrderSettlements => Set<OrderSettlement>();
    public DbSet<PlatformSubsidyBudget> PlatformSubsidyBudgets => Set<PlatformSubsidyBudget>();
    public DbSet<PlatformSubsidyReservation> PlatformSubsidyReservations => Set<PlatformSubsidyReservation>();
    public DbSet<RefundFundPayout> RefundFundPayouts => Set<RefundFundPayout>();

    public Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID must not be empty.", nameof(orderId));
        RequireTransaction("Order payment lock");
        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({orderId.ToString("D")}, 0))", cancellationToken);
    }

    public Task LockAuctionKeyAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Auction lock key must not be empty.", nameof(key));
        RequireTransaction("Auction lock");
        var namespacedKey = $"auction-hold:{key}";
        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({namespacedKey}, 0))", cancellationToken);
    }

    public Task<AuctionHold?> LockAuctionHoldAsync(Guid holdId, CancellationToken cancellationToken)
    {
        RequireTransaction("Auction hold lock");
        return AuctionHolds.FromSqlRaw(BuildLockQuery<AuctionHold>(nameof(AuctionHold.Id)), holdId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WalletAccount?> LockWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireTransaction("Wallet lock");
        return Wallets
            .FromSqlRaw(BuildLockQuery<WalletAccount>(nameof(WalletAccount.UserId)), userId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken)
    {
        RequireTransaction("Wallet lock");
        return Wallets
            .FromSqlRaw(BuildLockQuery<WalletAccount>(nameof(WalletAccount.Id)), walletId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WalletTopup?> LockTopupByIdAsync(Guid topupId, CancellationToken cancellationToken)
    {
        RequireTransaction("Topup lock");
        return WalletTopups
            .FromSqlRaw(BuildLockQuery<WalletTopup>(nameof(WalletTopup.Id)), topupId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<DirectPaymentOrder?> LockDirectPaymentOrderByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction("Direct payment order lock");
        return DirectPaymentOrders
            .FromSqlRaw(BuildLockQuery<DirectPaymentOrder>(nameof(DirectPaymentOrder.Id)), orderId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<StripePaymentAttempt?> LockStripePaymentAttemptAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        RequireTransaction("Stripe payment attempt lock");
        return StripePaymentAttempts
            .FromSqlRaw(BuildLockQuery<StripePaymentAttempt>(nameof(StripePaymentAttempt.Id)), attemptId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken)
    {
        RequireTransaction("Withdrawal request lock");
        return WithdrawalRequests
            .FromSqlRaw(BuildLockQuery<WithdrawalRequest>(nameof(WithdrawalRequest.Id)), withdrawalId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<OrderEscrow?> LockOrderEscrowByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction("Order escrow lock");
        return OrderEscrows.FromSqlRaw(BuildLockQuery<OrderEscrow>(nameof(OrderEscrow.OrderId)), orderId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuctionDepositEscrow>> LockDepositEscrowsByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        RequireTransaction("Deposit escrow lock");
        // FOR UPDATE on every row of the order, always in the same order, so two spenders of the same deposits queue instead of crossing.
        var rows = await AuctionDepositEscrows.FromSqlRaw(BuildLockQuery<AuctionDepositEscrow>(nameof(AuctionDepositEscrow.OrderId)), orderId)
            .ToListAsync(cancellationToken);
        return rows.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).ToList();
    }

    public Task<AuctionDepositEscrow?> LockDepositEscrowByHoldIdAsync(Guid holdId, CancellationToken cancellationToken)
    {
        RequireTransaction("Deposit escrow lock");
        return AuctionDepositEscrows.FromSqlRaw(BuildLockQuery<AuctionDepositEscrow>(nameof(AuctionDepositEscrow.HoldId)), holdId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<PlatformSubsidyBudget?> LockSubsidyBudgetAsync(Guid budgetId, CancellationToken cancellationToken)
    {
        RequireTransaction("Subsidy budget lock");
        return PlatformSubsidyBudgets.FromSqlRaw(BuildLockQuery<PlatformSubsidyBudget>(nameof(PlatformSubsidyBudget.Id)), budgetId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private void RequireTransaction(string lockName)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{lockName} requires a database transaction.");
    }

    public void EnsureNoPendingChanges()
    {
        if (ChangeTracker.HasChanges())
            throw new InvalidOperationException("Settlement requires a context without pending changes.");
    }

    public async Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken)
        where TEntity : class
    {
        ChangeTracker.DetectChanges();
        var entry = Entry(entity);
        if (entry.State != EntityState.Unchanged)
            throw new InvalidOperationException("Cannot reload a modified or untracked entity during settlement.");
        await entry.ReloadAsync(cancellationToken);
        if (entry.State == EntityState.Detached)
            throw new InvalidOperationException("Settlement entity no longer exists.");
    }

    private string BuildLockQuery<TEntity>(string propertyName)
        where TEntity : class
    {
        var entityType = Model.FindEntityType(typeof(TEntity))
                         ?? throw new InvalidOperationException(
                             $"Entity '{typeof(TEntity).Name}' is not in the EF model.");
        var tableName = entityType.GetTableName()
                        ?? throw new InvalidOperationException(
                            $"Entity '{typeof(TEntity).Name}' is not mapped to a table.");
        var table = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        var columnName = entityType.FindProperty(propertyName)?.GetColumnName(table)
                         ?? throw new InvalidOperationException(
                             $"Property '{propertyName}' is not mapped to a column.");
        var sql = Database.GetService<ISqlGenerationHelper>();
        return $"SELECT * FROM {sql.DelimitIdentifier(tableName, entityType.GetSchema())} " +
               $"WHERE {sql.DelimitIdentifier(columnName)} = {{0}} FOR UPDATE";
    }

    /// <summary>
    /// Shadow ledger chokepoint. When the ledger is on, every save that changes a wallet Balance must also carry a successful
    /// wallet transaction, and it gets a LedgerPostingPending row in the same database transaction. A save that changes Balance
    /// without one is refused, in every environment: that is what stops a balance from changing without leaving a trace.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await CaptureLedgerEventsAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task CaptureLedgerEventsAsync(CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries<WalletAccount>()
            .Where(entry => entry.State == EntityState.Modified
                && entry.OriginalValues.GetValue<decimal>(nameof(WalletAccount.Balance)) != entry.CurrentValues.GetValue<decimal>(nameof(WalletAccount.Balance)))
            .ToList();
        if (changed.Count == 0)
            return;

        var settings = await ReadLedgerSettingsAsync(cancellationToken);
        if (settings is null || !settings.Enabled)
            return;

        foreach (var entry in changed)
        {
            var wallet = entry.Entity;
            var before = entry.OriginalValues.GetValue<decimal>(nameof(WalletAccount.Balance));
            var transactions = ChangeTracker.Entries<WalletTransaction>()
                .Where(tx => tx.Entity.WalletId == wallet.Id
                    && tx.Entity.Status == Wallet.Domain.Enums.TransactionStatus.Success
                    && (tx.State == EntityState.Added
                        || (tx.State == EntityState.Modified
                            && tx.OriginalValues.GetValue<Wallet.Domain.Enums.TransactionStatus>(nameof(WalletTransaction.Status)) != Wallet.Domain.Enums.TransactionStatus.Success)))
                .Select(tx => tx.Entity)
                .ToList();
            if (transactions.Count == 0)
                throw new InvalidOperationException(
                    "Shadow ledger guard: the wallet balance changed without a successful wallet transaction in the same save.");

            if (wallet.LedgerEpoch != settings.ShadowEpoch)
            {
                // First event of this wallet since the ledger was switched on: open a new epoch that starts from the balance before the event.
                wallet.LedgerEpoch = settings.ShadowEpoch;
                wallet.LedgerSeq = 0;
                wallet.LedgerSeq++;
                LedgerPostingPending.Add(new LedgerPostingPending
                {
                    WalletId = wallet.Id,
                    LedgerEpoch = wallet.LedgerEpoch,
                    LedgerSeq = wallet.LedgerSeq,
                    IsEpochOpening = true,
                    Type = "EpochOpening",
                    Delta = 0m,
                    OpeningBalance = before,
                    WalletBalanceAfter = before
                });
            }

            wallet.LedgerSeq++;
            LedgerPostingPending.Add(new LedgerPostingPending
            {
                WalletId = wallet.Id,
                LedgerEpoch = wallet.LedgerEpoch,
                LedgerSeq = wallet.LedgerSeq,
                WalletTransactionId = transactions[0].Id,
                RelatedTransactionIds = string.Join(',', transactions.Select(tx => tx.Id)),
                Type = string.Join('+', transactions.Select(tx => tx.Type.ToString()).Distinct()),
                Delta = entry.CurrentValues.GetValue<decimal>(nameof(WalletAccount.Balance)) - before,
                WalletBalanceAfter = entry.CurrentValues.GetValue<decimal>(nameof(WalletAccount.Balance))
            });
        }
    }

    /// <summary>Reads the settings row with a shared lock held until the surrounding transaction ends, so the switch cannot flip between this read and the commit.</summary>
    private async Task<LedgerSettings?> ReadLedgerSettingsAsync(CancellationToken cancellationToken)
    {
        if (Database.IsRelational() && Database.CurrentTransaction is not null)
        {
            return await LedgerSettings
                .FromSqlRaw("SELECT * FROM \"LedgerSettings\" WHERE \"Id\" = 1 FOR SHARE")
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await LedgerSettings.AsNoTracking().FirstOrDefaultAsync(settings => settings.Id == 1, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
    }
}
