namespace Wallet.Infrastructure.Persistence;

using MassTransit;
using Microsoft.EntityFrameworkCore;
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
    public DbSet<DirectPaymentOrder> DirectPaymentOrders => Set<DirectPaymentOrder>();
    public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();
    public DbSet<VnpayIpnLog> VnpayIpnLogs => Set<VnpayIpnLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
        Seeds.WalletSeedData.SeedAll(modelBuilder);

        // MassTransit Transactional Outbox entities
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
