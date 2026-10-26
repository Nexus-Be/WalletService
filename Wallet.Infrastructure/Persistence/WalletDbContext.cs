namespace Wallet.Infrastructure.Persistence;

using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Wallet.Application.Common.Interfaces;
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
    public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();
    public DbSet<VnpayIpnLog> VnpayIpnLogs => Set<VnpayIpnLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

    public Task<WalletTopup?> LockTopupByReferenceAsync(string transactionReference,
        CancellationToken cancellationToken)
    {
        return WalletTopups
            .FromSqlRaw(BuildLockQuery<WalletTopup>(nameof(WalletTopup.VnpTxnRef)), transactionReference)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<WithdrawalRequest?> LockWithdrawalRequestAsync(Guid withdrawalId, CancellationToken cancellationToken)
    {
        return WithdrawalRequests
            .FromSqlRaw(BuildLockQuery<WithdrawalRequest>(nameof(WithdrawalRequest.Id)), withdrawalId)
            .SingleOrDefaultAsync(cancellationToken);
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

        // MassTransit Transactional Outbox entities
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

public class AppDbContext : WalletDbContext
{
    public AppDbContext(DbContextOptions<WalletDbContext> options) : base(options)
    {
    }
}