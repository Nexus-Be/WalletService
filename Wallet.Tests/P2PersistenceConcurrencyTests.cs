namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Features.Wallets.Commands.EnsureWallet;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

public sealed class P2PersistenceConcurrencyTests
{
    [Fact]
    public async Task WalletDbContext_AllRowLockMethods_RequireActiveTransaction()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=wallet_lock_guard_test;Username=test;Password=test")
            .Options;
        await using var context = new WalletDbContext(options);

        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockOrderPaymentAsync(id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockAuctionKeyAsync($"key:{id:N}", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockAuctionHoldAsync(id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockWalletByUserIdAsync(id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockWalletByIdAsync(id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockTopupByIdAsync(id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockDirectPaymentOrderByIdAsync(id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockStripePaymentAttemptAsync(id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.LockWithdrawalRequestAsync(id, default));
    }

    [Fact]
    public void DesignTimeModel_AlignsFilteredUniqueIndexesAndDropsRedundantWalletConstraint()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql("Host=localhost;Database=wallet_model_p2_test;Username=test;Password=test")
            .Options;
        using var context = new WalletDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;

        var walletEntity = model.FindEntityType(typeof(WalletAccount))!;
        Assert.Contains(walletEntity.GetCheckConstraints(), c => c.Name == "CK_Wallets_BalanceLocks");
        Assert.DoesNotContain(walletEntity.GetCheckConstraints(), c => c.Name == "CK_Wallets_LockedBalance");
        var walletUserIndex = Assert.Single(walletEntity.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(WalletAccount.UserId) }));
        Assert.Equal("\"IsDeleted\" = FALSE", walletUserIndex.GetFilter());

        var directOrderEntity = model.FindEntityType(typeof(DirectPaymentOrder))!;
        var directOrderIdIndex = Assert.Single(directOrderEntity.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(DirectPaymentOrder.OrderId) }));
        Assert.Equal("\"IsDeleted\" = FALSE", directOrderIdIndex.GetFilter());
        var directVnpRefIndex = Assert.Single(directOrderEntity.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(DirectPaymentOrder.VnpTxnRef) }));
        Assert.Equal("\"VnpTxnRef\" IS NOT NULL AND \"IsDeleted\" = FALSE", directVnpRefIndex.GetFilter());

        var topupEntity = model.FindEntityType(typeof(WalletTopup))!;
        var topupVnpRefIndex = Assert.Single(topupEntity.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(WalletTopup.VnpTxnRef) }));
        Assert.Equal("\"VnpTxnRef\" IS NOT NULL AND \"IsDeleted\" = FALSE", topupVnpRefIndex.GetFilter());

        var escrowEntity = model.FindEntityType(typeof(OrderEscrow))!;
        var escrowOrderIndex = Assert.Single(escrowEntity.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(OrderEscrow.OrderId) }));
        Assert.Null(escrowOrderIndex.GetFilter());

        var webhookEntity = model.FindEntityType(typeof(StripeWebhookEvent))!;
        var webhookEventIndex = Assert.Single(webhookEntity.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(StripeWebhookEvent.ProviderEventId) }));
        Assert.Null(webhookEventIndex.GetFilter());
    }

    [Fact]
    public async Task LockOrEnsureWalletByUserIdAsync_AcquiresAdvisoryLockAndRechecksBeforeCreating()
    {
        var dbName = nameof(LockOrEnsureWalletByUserIdAsync_AcquiresAdvisoryLockAndRechecksBeforeCreating);
        var existingUserId = Guid.NewGuid();
        var racedUserId = Guid.NewGuid();
        var freshUserId = Guid.NewGuid();

        using var seedContext = new TestWalletDbContext(dbName);
        var preExisting = new WalletAccount { Id = Guid.NewGuid(), UserId = existingUserId, Currency = CurrencyCode.Vnd };
        seedContext.Wallets.Add(preExisting);
        await seedContext.SaveChangesAsync();

        // 1. Existing wallet does not acquire the creation advisory lock.
        using (var existingInner = new TestWalletDbContext(dbName))
        {
            var existingWrapper = new RacingWalletDbContext(existingInner);
            var found = await existingWrapper.LockOrEnsureWalletByUserIdAsync(existingUserId, default);
            Assert.Equal(preExisting.Id, found.Id);
            Assert.Empty(existingWrapper.LockKeys);
        }

        // 2. Competing wallet inserted while acquiring wallet-create lock is returned without creating a duplicate or AuditLog.
        var competingWallet = new WalletAccount { Id = Guid.NewGuid(), UserId = racedUserId, Currency = CurrencyCode.Vnd };
        using (var racedInner = new TestWalletDbContext(dbName))
        {
            var racedWrapper = new RacingWalletDbContext(
                racedInner,
                onLockAuctionKey: _ =>
                {
                    using var other = new TestWalletDbContext(dbName);
                    other.Wallets.Add(competingWallet);
                    other.SaveChanges();
                });

            var resolved = await racedWrapper.LockOrEnsureWalletByUserIdAsync(racedUserId, default);
            Assert.Equal(competingWallet.Id, resolved.Id);
            Assert.Equal(new[] { $"wallet-create:{racedUserId:D}" }, racedWrapper.LockKeys);
            Assert.Empty(racedInner.AuditLogs.Where(a => a.UserId == racedUserId.ToString()));
        }

        // 3. Truly brand-new user acquires wallet-create lock and persists one wallet + one WalletCreate audit log.
        using (var freshInner = new TestWalletDbContext(dbName))
        {
            var freshWrapper = new RacingWalletDbContext(freshInner);
            var created = await freshWrapper.LockOrEnsureWalletByUserIdAsync(freshUserId, default);
            Assert.Equal(freshUserId, created.UserId);
            Assert.Equal(new[] { $"wallet-create:{freshUserId:D}" }, freshWrapper.LockKeys);
            Assert.Single(freshInner.AuditLogs.Where(a =>
                a.Action == AuditAction.WalletCreate && a.UserId == freshUserId.ToString()));
        }
    }

    [Fact]
    public async Task EnsureWalletCommandHandler_DetachesBothWalletAndAuditLogOnDbUpdateException()
    {
        var dbName = nameof(EnsureWalletCommandHandler_DetachesBothWalletAndAuditLogOnDbUpdateException);
        var userId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();

        using var context = new FailingOnceEnsureWalletDbContext(dbName, userId, winnerId);
        var response = await new EnsureWalletCommandHandler(context).Handle(new EnsureWalletCommand(userId), default);

        Assert.Equal(winnerId.ToString(), response.WalletId);
        Assert.Equal($"wallet-create:{userId:D}", Assert.Single(context.LockKeys));
        Assert.Empty(context.AuditLogs);
        Assert.Empty(context.WalletTrackerEntries());
        Assert.Empty(context.AuditTrackerEntries());
    }

    [Fact]
    public async Task StripeTopupPreparation_DetectsConflictAgainstSoftDeletedAttempt()
    {
        using var context = new TestWalletDbContext(nameof(StripeTopupPreparation_DetectsConflictAgainstSoftDeletedAttempt));
        var userId = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = userId, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            Status = TopupStatus.Pending,
            IsDeleted = true
        };
        var deletedAttempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Purpose = StripePaymentPurpose.Topup,
            WalletTopupId = topup.Id,
            Amount = 100_000m,
            RequestFingerprint = "fp",
            ClientIdempotencyKey = "soft-deleted-key",
            ProviderIdempotencyKey = "pk-soft-deleted",
            Status = StripePaymentAttemptStatus.AwaitingPayment,
            BlocksReplacement = true,
            CheckoutUrl = "https://checkout.stripe.com/deleted",
            SessionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            IsDeleted = true
        };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(deletedAttempt);
        await context.SaveChangesAsync();

        var err = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeTopupPreparation.CreateAsync(
                context,
                new NoCallStripeClient(),
                userId,
                100_000m,
                "soft-deleted-key",
                default));
        Assert.Equal("PAYMENT_CONFLICT", err.ErrorCode);
        Assert.Equal(409, err.StatusCode);
    }

    private sealed class RacingWalletDbContext(
        TestWalletDbContext inner,
        Action<string>? onLockAuctionKey = null) : IWalletDbContext
    {
        public List<string> LockKeys { get; } = [];

        public DbSet<WalletAccount> Wallets => inner.Wallets;
        public DbSet<AuctionHold> AuctionHolds => inner.AuctionHolds;
        public DbSet<AuctionHoldOperation> AuctionHoldOperations => inner.AuctionHoldOperations;
        public DbSet<AuctionHoldCapture> AuctionHoldCaptures => inner.AuctionHoldCaptures;
        public DbSet<AuctionCaptureTransfer> AuctionCaptureTransfers => inner.AuctionCaptureTransfers;
        public DbSet<AuctionDepositEscrow> AuctionDepositEscrows => inner.AuctionDepositEscrows;
        public DbSet<OrderEscrow> OrderEscrows => inner.OrderEscrows;
        public DbSet<WalletTransaction> WalletTransactions => inner.WalletTransactions;
        public DbSet<BankAccount> BankAccounts => inner.BankAccounts;
        public DbSet<WalletTopup> WalletTopups => inner.WalletTopups;
        public DbSet<DirectPaymentOrder> DirectPaymentOrders => inner.DirectPaymentOrders;
        public DbSet<StripePaymentAttempt> StripePaymentAttempts => inner.StripePaymentAttempts;
        public DbSet<StripeWebhookEvent> StripeWebhookEvents => inner.StripeWebhookEvents;
        public DbSet<VnpayIpnLog> VnpayIpnLogs => inner.VnpayIpnLogs;
        public DbSet<WithdrawalRequest> WithdrawalRequests => inner.WithdrawalRequests;
        public DbSet<AuditLog> AuditLogs => inner.AuditLogs;
        public DbSet<LedgerSettings> LedgerSettings => inner.LedgerSettings;
        public DbSet<LedgerPostingPending> LedgerPostingPending => inner.LedgerPostingPending;
        public DbSet<LedgerAccount> LedgerAccounts => inner.LedgerAccounts;
        public DbSet<JournalEntry> JournalEntries => inner.JournalEntries;
        public DbSet<JournalLine> JournalLines => inner.JournalLines;
        public DbSet<ReconciliationRun> ReconciliationRuns => inner.ReconciliationRuns;
        public DbSet<ReconciliationFinding> ReconciliationFindings => inner.ReconciliationFindings;
        public DbSet<OrderSettlement> OrderSettlements => inner.OrderSettlements;
        public DbSet<PlatformSubsidyBudget> PlatformSubsidyBudgets => inner.PlatformSubsidyBudgets;
        public DbSet<PlatformSubsidyReservation> PlatformSubsidyReservations => inner.PlatformSubsidyReservations;
        public DbSet<RefundFundPayout> RefundFundPayouts => inner.RefundFundPayouts;
        public DatabaseFacade Database => inner.Database;

        public Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken) =>
            inner.LockOrderPaymentAsync(orderId, cancellationToken);

        public Task LockAuctionKeyAsync(string key, CancellationToken cancellationToken)
        {
            LockKeys.Add(key);
            onLockAuctionKey?.Invoke(key);
            return inner.LockAuctionKeyAsync(key, cancellationToken);
        }

        public Task<AuctionHold?> LockAuctionHoldAsync(Guid holdId, CancellationToken cancellationToken) =>
            inner.LockAuctionHoldAsync(holdId, cancellationToken);
        public Task<WalletAccount?> LockWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.LockWalletByUserIdAsync(userId, cancellationToken);
        public Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken) =>
            inner.LockWalletByIdAsync(walletId, cancellationToken);
        public Task<WalletTopup?> LockTopupByIdAsync(Guid topupId, CancellationToken cancellationToken) =>
            inner.LockTopupByIdAsync(topupId, cancellationToken);
        public Task<DirectPaymentOrder?> LockDirectPaymentOrderByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            inner.LockDirectPaymentOrderByIdAsync(orderId, cancellationToken);
        public Task<StripePaymentAttempt?> LockStripePaymentAttemptAsync(Guid attemptId, CancellationToken cancellationToken) =>
            inner.LockStripePaymentAttemptAsync(attemptId, cancellationToken);
        public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken) =>
            inner.LockWithdrawalRequestAsync(withdrawalId, cancellationToken);
        public Task<OrderEscrow?> LockOrderEscrowByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            inner.LockOrderEscrowByOrderIdAsync(orderId, cancellationToken);
        public Task<IReadOnlyList<AuctionDepositEscrow>> LockDepositEscrowsByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            inner.LockDepositEscrowsByOrderIdAsync(orderId, cancellationToken);
        public Task<AuctionDepositEscrow?> LockDepositEscrowByHoldIdAsync(Guid holdId, CancellationToken cancellationToken) =>
            inner.LockDepositEscrowByHoldIdAsync(holdId, cancellationToken);
        public Task<PlatformSubsidyBudget?> LockSubsidyBudgetAsync(Guid budgetId, CancellationToken cancellationToken) =>
            inner.LockSubsidyBudgetAsync(budgetId, cancellationToken);
        public void EnsureNoPendingChanges() => inner.EnsureNoPendingChanges();
        public Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class =>
            inner.ReloadUnmodifiedAsync(entity, cancellationToken);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class FailingOnceEnsureWalletDbContext(
        string databaseName,
        Guid userId,
        Guid winnerWalletId) : IWalletDbContext, IDisposable
    {
        private readonly WalletDbContext _inner = new(new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        private bool _failedOnce;

        public List<string> LockKeys { get; } = [];
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

        public IReadOnlyList<WalletAccount> WalletTrackerEntries() =>
            _inner.ChangeTracker.Entries<WalletAccount>().Select(e => e.Entity).ToArray();

        public IReadOnlyList<AuditLog> AuditTrackerEntries() =>
            _inner.ChangeTracker.Entries<AuditLog>().Select(e => e.Entity).ToArray();

        public Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LockAuctionKeyAsync(string key, CancellationToken cancellationToken)
        {
            LockKeys.Add(key);
            return Task.CompletedTask;
        }

        public Task<AuctionHold?> LockAuctionHoldAsync(Guid holdId, CancellationToken cancellationToken) =>
            _inner.AuctionHolds.SingleOrDefaultAsync(h => h.Id == holdId, cancellationToken);
        public Task<WalletAccount?> LockWalletByUserIdAsync(Guid id, CancellationToken cancellationToken) =>
            _inner.Wallets.SingleOrDefaultAsync(w => w.UserId == id, cancellationToken);
        public Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken) =>
            _inner.Wallets.SingleOrDefaultAsync(w => w.Id == walletId, cancellationToken);
        public Task<WalletTopup?> LockTopupByIdAsync(Guid topupId, CancellationToken cancellationToken) =>
            _inner.WalletTopups.SingleOrDefaultAsync(t => t.Id == topupId, cancellationToken);
        public Task<DirectPaymentOrder?> LockDirectPaymentOrderByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            _inner.DirectPaymentOrders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        public Task<StripePaymentAttempt?> LockStripePaymentAttemptAsync(Guid attemptId, CancellationToken cancellationToken) =>
            _inner.StripePaymentAttempts.SingleOrDefaultAsync(a => a.Id == attemptId, cancellationToken);
        public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken) =>
            _inner.WithdrawalRequests.SingleOrDefaultAsync(r => r.Id == withdrawalId, cancellationToken);
        public Task<OrderEscrow?> LockOrderEscrowByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            _inner.OrderEscrows.SingleOrDefaultAsync(e => e.OrderId == orderId, cancellationToken);
        public async Task<IReadOnlyList<AuctionDepositEscrow>> LockDepositEscrowsByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            (await _inner.AuctionDepositEscrows.Where(e => e.OrderId == orderId).ToListAsync(cancellationToken))
                .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList();
        public Task<AuctionDepositEscrow?> LockDepositEscrowByHoldIdAsync(Guid holdId, CancellationToken cancellationToken) =>
            _inner.AuctionDepositEscrows.SingleOrDefaultAsync(e => e.HoldId == holdId, cancellationToken);
        public Task<PlatformSubsidyBudget?> LockSubsidyBudgetAsync(Guid budgetId, CancellationToken cancellationToken) =>
            _inner.PlatformSubsidyBudgets.SingleOrDefaultAsync(b => b.Id == budgetId, cancellationToken);
        public void EnsureNoPendingChanges() => _inner.EnsureNoPendingChanges();
        public Task ReloadUnmodifiedAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class =>
            _inner.ReloadUnmodifiedAsync(entity, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_failedOnce)
            {
                _failedOnce = true;
                using var other = new WalletDbContext(new DbContextOptionsBuilder<WalletDbContext>()
                    .UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options);
                other.Wallets.Add(new WalletAccount
                {
                    Id = winnerWalletId,
                    UserId = userId,
                    Currency = CurrencyCode.Vnd,
                    Status = "ACTIVE"
                });
                other.SaveChanges();
                throw new DbUpdateException("Simulated unique violation");
            }

            return _inner.SaveChangesAsync(cancellationToken);
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class NoCallStripeClient : IStripeCheckoutClient
    {
        public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StripeSessionSnapshot> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
