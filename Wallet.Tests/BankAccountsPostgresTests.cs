namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.SetDefaultBankAccount;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class BankAccountsPostgresTests
{
    [DisposablePostgresFact]
    public async Task ConcurrentCreateAndDeleteBankAccounts_SerializedByAdvisoryLock_NeverViolateUniqueIndex()
    {
        var (options, userId) = await SetupUserAsync();

        Guid initialId;
        await using (var setup = new WalletDbContext(options))
        {
            var initial = await new CreateBankAccountCommandHandler(setup).Handle(
                new CreateBankAccountCommand("VCB", "1000000001", "Seller One", IsDefault: true) { UserId = userId },
                default);
            initialId = initial.Id;
        }

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;

        async Task RunCreateAsync(string bank, string number, bool isDefault)
        {
            await using var context = new WalletDbContext(options);
            if (Interlocked.Increment(ref arrived) == 4)
            {
                barrier.TrySetResult();
            }

            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await new CreateBankAccountCommandHandler(context).Handle(
                new CreateBankAccountCommand(bank, number, "Seller One", isDefault) { UserId = userId },
                default);
        }

        async Task RunDeleteAsync(Guid id)
        {
            await using var context = new WalletDbContext(options);
            if (Interlocked.Increment(ref arrived) == 4)
            {
                barrier.TrySetResult();
            }

            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await new DeleteBankAccountCommandHandler(context).Handle(
                new DeleteBankAccountCommand(id) { UserId = userId },
                default);
        }

        await Task.WhenAll(
            RunCreateAsync("ACB", "2000000002", true),
            RunCreateAsync("TPB", "3000000003", true),
            RunCreateAsync("MB", "4000000004", false),
            RunDeleteAsync(initialId));

        await using var verify = new WalletDbContext(options);
        var active = await verify.BankAccounts.AsNoTracking()
            .Where(item => item.UserId == userId.ToString())
            .ToListAsync();

        Assert.Equal(3, active.Count);
        Assert.Single(active, item => item.IsDefault);
    }

    [DisposablePostgresFact]
    public async Task SetDefaultBankAccount_BidirectionalIdOrder_NeverViolatesPartialUniqueIndex()
    {
        var (options, userId) = await SetupUserAsync();
        var ids = new List<Guid>();

        await using (var setup = new WalletDbContext(options))
        {
            var create = new CreateBankAccountCommandHandler(setup);
            ids.Add((await create.Handle(new CreateBankAccountCommand("VCB", "5000000001", "Seller One", IsDefault: true) { UserId = userId }, default)).Id);
            ids.Add((await create.Handle(new CreateBankAccountCommand("ACB", "5000000002", "Seller One", IsDefault: false) { UserId = userId }, default)).Id);
            ids.Add((await create.Handle(new CreateBankAccountCommand("BIDV", "5000000003", "Seller One", IsDefault: false) { UserId = userId }, default)).Id);
        }

        var sorted = ids.OrderBy(id => id).ToArray();
        var minId = sorted[0];
        var midId = sorted[1];
        var maxId = sorted[2];

        foreach (var targetId in new[] { minId, midId, maxId, minId, maxId })
        {
            await using var context = new WalletDbContext(options);
            var updated = await new SetDefaultBankAccountCommandHandler(context).Handle(
                new SetDefaultBankAccountCommand(targetId) { UserId = userId },
                default);
            Assert.True(updated.IsDefault);
            Assert.Equal(targetId, updated.Id);

            await using var check = new WalletDbContext(options);
            var active = await check.BankAccounts.AsNoTracking()
                .Where(item => item.UserId == userId.ToString())
                .ToListAsync();
            var defaultAccount = Assert.Single(active, item => item.IsDefault);
            Assert.Equal(targetId, defaultAccount.Id);
        }
    }

    private static async Task<(DbContextOptions<WalletDbContext> Options, Guid UserId)> SetupUserAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var userId = Guid.NewGuid();

        await using var context = new WalletDbContext(options);
        await context.Database.MigrateAsync();
        context.Wallets.Add(new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = 500_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        });
        await context.SaveChangesAsync();
        return (options, userId);
    }

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")))
            {
                Skip = "Set WALLET_STRIPE_TEST_DATABASE to a disposable PostgreSQL database to run this integration test.";
            }
        }
    }
}
