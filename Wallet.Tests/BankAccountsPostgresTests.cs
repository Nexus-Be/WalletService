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
    public async Task BankAccounts_SwitchDefaultCreateAndDeletePromote_NeverViolateUniqueIndex()
    {
        var (options, userId) = await SetupUserAsync();

        Guid lowerId;
        Guid higherId;
        await using (var context = new WalletDbContext(options))
        {
            var create = new CreateBankAccountCommandHandler(context);
            var a = await create.Handle(
                new CreateBankAccountCommand("VCB", "5000000001", "Seller One", IsDefault: true) { UserId = userId },
                default);
            var b = await create.Handle(
                new CreateBankAccountCommand("ACB", "5000000002", "Seller One", IsDefault: true) { UserId = userId },
                default);
            var c = await create.Handle(
                new CreateBankAccountCommand("TPB", "5000000003", "Seller One", IsDefault: false) { UserId = userId },
                default);

            var sorted = new[] { a.Id, b.Id, c.Id }.OrderBy(id => id).ToArray();
            lowerId = sorted[0];
            higherId = sorted[2];
        }

        // Flip default in both UUID directions (lower -> higher and higher -> lower) on real PostgreSQL.
        await using (var context = new WalletDbContext(options))
        {
            var setDefault = new SetDefaultBankAccountCommandHandler(context);
            await setDefault.Handle(new SetDefaultBankAccountCommand(higherId) { UserId = userId }, default);
            await setDefault.Handle(new SetDefaultBankAccountCommand(lowerId) { UserId = userId }, default);
            await setDefault.Handle(new SetDefaultBankAccountCommand(higherId) { UserId = userId }, default);
        }

        // Deleting the active default must promote a replacement without tripping IX_BankAccounts_UserId_Default.
        await using (var context = new WalletDbContext(options))
        {
            await new DeleteBankAccountCommandHandler(context)
                .Handle(new DeleteBankAccountCommand(higherId) { UserId = userId }, default);
        }

        await using var verify = new WalletDbContext(options);
        var active = await verify.BankAccounts.AsNoTracking()
            .Where(item => item.UserId == userId.ToString())
            .ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.Single(active, item => item.IsDefault);
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
}
