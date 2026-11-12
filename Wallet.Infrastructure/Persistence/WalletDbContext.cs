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
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<WalletTopup> WalletTopups => Set<WalletTopup>();
    public DbSet<DirectPaymentOrder> DirectPaymentOrders => Set<DirectPaymentOrder>();
    public DbSet<StripePaymentAttempt> StripePaymentAttempts => Set<StripePaymentAttempt>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();
    public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();
    public DbSet<VnpayIpnLog> VnpayIpnLogs => Set<VnpayIpnLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public Task LockOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID must not be empty.", nameof(orderId));
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Order payment lock requires a transaction.");
        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({orderId.ToString("D")}, 0))", cancellationToken);
    }

    public Task<WalletAccount?> LockWalletByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return Wallets
            .FromSqlRaw(BuildLockQuery<WalletAccount>(nameof(WalletAccount.UserId)), userId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WalletAccount?> LockWalletByIdAsync(Guid walletId, CancellationToken cancellationToken)
    {
        return Wallets
            .FromSqlRaw(BuildLockQuery<WalletAccount>(nameof(WalletAccount.Id)), walletId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WalletTopup?> LockTopupByIdAsync(Guid topupId, CancellationToken cancellationToken)
    {
        return WalletTopups
            .FromSqlRaw(BuildLockQuery<WalletTopup>(nameof(WalletTopup.Id)), topupId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<DirectPaymentOrder?> LockDirectPaymentOrderByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return DirectPaymentOrders
            .FromSqlRaw(BuildLockQuery<DirectPaymentOrder>(nameof(DirectPaymentOrder.Id)), orderId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<StripePaymentAttempt?> LockStripePaymentAttemptAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        return StripePaymentAttempts
            .FromSqlRaw(BuildLockQuery<StripePaymentAttempt>(nameof(StripePaymentAttempt.Id)), attemptId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WalletTopup?> LockTopupByReferenceAsync(string transactionReference,
        CancellationToken cancellationToken)
    {
        return WalletTopups
            .FromSqlRaw(BuildLockQuery<WalletTopup>(nameof(WalletTopup.VnpTxnRef)), transactionReference)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<DirectPaymentOrder?> LockDirectPaymentOrderByReferenceAsync(
        string transactionReference,
        CancellationToken cancellationToken)
    {
        return DirectPaymentOrders
            .FromSqlRaw(
                BuildLockQuery<DirectPaymentOrder>(nameof(DirectPaymentOrder.VnpTxnRef)),
                transactionReference)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken)
    {
        return WithdrawalRequests
            .FromSqlRaw(BuildLockQuery<WithdrawalRequest>(nameof(WithdrawalRequest.Id)), withdrawalId)
            .SingleOrDefaultAsync(cancellationToken);
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
    }
}
