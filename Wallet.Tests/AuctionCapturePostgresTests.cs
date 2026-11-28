namespace Wallet.Tests;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Wallet.Application.Features.Holds.Commands.CreateHold;
using Wallet.Application.Features.Holds.Commands.RefundCapturedHold;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Wallet.Application.Features.Holds.Queries.GetHold;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Persistence;
using Wallet.Tests.Support;
using Xunit;

[Collection(PostgresTestCollection.Name)]
public sealed class AuctionCapturePostgresTests
{
    [DisposablePostgresFact]
    public async Task OrderCaptureAndConcurrentRefund_ConserveFundsAndReplayAfterRefund()
    {
        var (options, buyer, holdId) = await Setup();
        var order = Guid.NewGuid();
        var capture = new CaptureHoldCommand("ORDER_PAYMENT", order, [new("SYSTEM_ESCROW", null, 50)])
            { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" };
        await using var a = new WalletDbContext(options);
        await using var b = new WalletDbContext(options);
        var captures = await Task.WhenAll(new CaptureHoldCommandHandler(a).Handle(capture, default),
            new CaptureHoldCommandHandler(b).Handle(capture, default));
        Assert.Equal(JsonSerializer.Serialize(captures[0]), JsonSerializer.Serialize(captures[1]));
        var refund = new RefundCapturedHoldCommand("SELLER_REJECTED", order)
            { HoldId = holdId, IdempotencyKey = $"refund:{holdId}" };
        await using var c = new WalletDbContext(options);
        await using var d = new WalletDbContext(options);
        var refunds = await Task.WhenAll(new RefundCapturedHoldCommandHandler(c).Handle(refund, default),
            new RefundCapturedHoldCommandHandler(d).Handle(refund with { IdempotencyKey = $"alternate:{holdId}" }, default));
        Assert.Equal(refunds[0], refunds[1]);
        await using var verify = new WalletDbContext(options);
        var replay = await new CaptureHoldCommandHandler(verify).Handle(capture with { IdempotencyKey = $"capture-again:{holdId}" }, default);
        Assert.Equal(JsonSerializer.Serialize(captures[0]), JsonSerializer.Serialize(replay));
        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == buyer);
        Assert.Equal(100, wallet.Balance);
        Assert.Equal(0, wallet.LockedBalance);
        Assert.Equal(0, (await verify.AuctionDepositEscrows.SingleAsync(e => e.HoldId == holdId)).RemainingAmount);
        Assert.Equal(2, await verify.WalletTransactions.CountAsync(t => t.ReferenceId == holdId));
        var status = await new GetHoldQueryHandler(verify).Handle(new GetHoldQuery(holdId, null, null), default);
        Assert.Equal("Refunded", status.Status);
        Assert.Equal("ORDER_PAYMENT", status.CapturePurpose);
    }

    [DisposablePostgresFact]
    public async Task Forfeit_ConservesMoneyAndRejectsChangedReplayOrRefund()
    {
        var (options, buyer, holdId) = await Setup();
        var seller = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            setup.Wallets.Add(new WalletAccount { Id = Guid.NewGuid(), UserId = seller, Balance = 20, Currency = CurrencyCode.Vnd });
            await setup.SaveChangesAsync();
        }
        var request = new CaptureHoldCommand("FORFEIT", null,
            [new("SELLER_WALLET", seller, 35), new("PLATFORM", null, 15)])
            { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" };
        await using (var context = new WalletDbContext(options))
            await new CaptureHoldCommandHandler(context).Handle(request, default);
        await using (var other = new WalletDbContext(options))
        {
            var replay = await new CaptureHoldCommandHandler(other).Handle(request with
                { IdempotencyKey = $"other:{holdId}", Transfers = request.Transfers.Reverse().ToArray() }, default);
            Assert.Equal(50, replay.BuyerBalanceAfter);
        }
        await using (var conflict = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                new CaptureHoldCommandHandler(conflict).Handle(request with
                {
                    IdempotencyKey = $"conflict:{holdId}",
                    Transfers = [new("SELLER_WALLET", seller, 30), new("PLATFORM", null, 20)]
                }, default));
            Assert.Equal("HOLD_ALREADY_CAPTURED_DIFFERENT", error.ErrorCode);
        }
        await using (var refund = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                new RefundCapturedHoldCommandHandler(refund).Handle(new("SELLER_REJECTED", Guid.NewGuid())
                    { HoldId = holdId, IdempotencyKey = $"refund:{holdId}" }, default));
            Assert.Equal("HOLD_NOT_REFUNDABLE", error.ErrorCode);
        }
        await using var verify = new WalletDbContext(options);
        Assert.Equal(50, (await verify.Wallets.SingleAsync(w => w.UserId == buyer)).Balance);
        Assert.Equal(55, (await verify.Wallets.SingleAsync(w => w.UserId == seller)).Balance);
        Assert.Equal(15, await verify.AuctionCaptureTransfers.Where(t =>
            t.RecipientType == "PLATFORM" && t.CaptureId == verify.AuctionHoldCaptures.Where(c => c.HoldId == holdId).Select(c => c.Id).Single())
            .SumAsync(t => t.Amount));
        Assert.Equal(0, await verify.AuctionDepositEscrows.CountAsync(e => e.HoldId == holdId));
    }

    [DisposablePostgresFact]
    public async Task InvalidCapture_RollsBackAllMoneyAndCanBeRetried()
    {
        var (options, buyer, holdId) = await Setup();
        var missing = Guid.NewGuid();
        var request = new CaptureHoldCommand("FORFEIT", null, [new("SELLER_WALLET", missing, 50)])
            { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" };
        await using (var context = new WalletDbContext(options))
            await Assert.ThrowsAsync<WalletNotFoundException>(() => new CaptureHoldCommandHandler(context).Handle(request, default));
        await using var verify = new WalletDbContext(options);
        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == buyer);
        Assert.Equal(100, wallet.Balance);
        Assert.Equal(50, wallet.LockedBalance);
        Assert.Equal(AuctionHoldStatus.Held, (await verify.AuctionHolds.SingleAsync(h => h.Id == holdId)).Status);
        Assert.Equal(0, await verify.AuctionHoldCaptures.CountAsync(c => c.HoldId == holdId));
        Assert.Equal(0, await verify.AuctionHoldOperations.CountAsync(o => o.IdempotencyKey == request.IdempotencyKey));
    }

    [DisposablePostgresFact]
    public async Task CaptureAndRelease_RaceHasOneWinner()
    {
        var (options, buyer, holdId) = await Setup();
        var capture = new CaptureHoldCommand("FORFEIT", null, [new("PLATFORM", null, 50)])
            { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" };
        var release = new ReleaseHoldCommand("AUCTION_CANCELLED") { HoldId = holdId, IdempotencyKey = $"release:{holdId}" };
        await using var a = new WalletDbContext(options);
        await using var b = new WalletDbContext(options);
        var outcomes = await Task.WhenAll(Attempt(() => new CaptureHoldCommandHandler(a).Handle(capture, default)),
            Attempt(() => new ReleaseHoldCommandHandler(b).Handle(release, default)));
        Assert.Single(outcomes, succeeded => succeeded);
        await using var verify = new WalletDbContext(options);
        var wallet = await verify.Wallets.SingleAsync(w => w.UserId == buyer);
        Assert.Equal(0, wallet.LockedBalance);
        Assert.Contains(wallet.Balance, new[] { 50m, 100m });
    }

    [DisposablePostgresFact]
    public async Task FrozenBuyer_CanCaptureAndRefund_WithoutSpendingOtherReservations()
    {
        var (options, buyer, holdId) = await Setup();
        await using (var setup = new WalletDbContext(options))
        {
            var wallet = await setup.Wallets.SingleAsync(w => w.UserId == buyer);
            wallet.Status = "SUSPENDED";
            wallet.LockedBalance += 20;
            await setup.SaveChangesAsync();
        }
        var order = Guid.NewGuid();
        await using (var context = new WalletDbContext(options))
            await new CaptureHoldCommandHandler(context).Handle(
                new CaptureHoldCommand("ORDER_PAYMENT", order, [new("SYSTEM_ESCROW", null, 50)])
                { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" }, default);
        await using (var context = new WalletDbContext(options))
            await new RefundCapturedHoldCommandHandler(context).Handle(
                new RefundCapturedHoldCommand("SELLER_REJECTED", order)
                { HoldId = holdId, IdempotencyKey = $"refund:{holdId}" }, default);
        await using var verify = new WalletDbContext(options);
        var walletAfter = await verify.Wallets.SingleAsync(w => w.UserId == buyer);
        Assert.Equal(100, walletAfter.Balance);
        Assert.Equal(20, walletAfter.LockedBalance);
    }

    [DisposablePostgresFact]
    public async Task InvalidAmountsAndOrders_DoNotMoveMoneyOrConsumeKey()
    {
        var (options, buyer, holdId) = await Setup();
        var order = Guid.NewGuid();
        var request = new CaptureHoldCommand("ORDER_PAYMENT", order, [new("SYSTEM_ESCROW", null, 49)])
            { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" };
        await using (var context = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                new CaptureHoldCommandHandler(context).Handle(request, default));
            Assert.Equal("TRANSFERS_SUM_MISMATCH", error.ErrorCode);
        }
        await using (var context = new WalletDbContext(options))
            await new CaptureHoldCommandHandler(context).Handle(request with
                { Transfers = [new("SYSTEM_ESCROW", null, 50)] }, default);
        await using (var context = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                new RefundCapturedHoldCommandHandler(context).Handle(new("SELLER_REJECTED", Guid.NewGuid())
                    { HoldId = holdId, IdempotencyKey = $"refund:{holdId}" }, default));
            Assert.Equal("HOLD_NOT_REFUNDABLE", error.ErrorCode);
        }
        await using var verify = new WalletDbContext(options);
        Assert.Equal(50, (await verify.Wallets.SingleAsync(w => w.UserId == buyer)).Balance);
        Assert.Equal(50, (await verify.AuctionDepositEscrows.SingleAsync(e => e.HoldId == holdId)).RemainingAmount);
        Assert.Equal(0, await verify.AuctionHoldOperations.CountAsync(o => o.IdempotencyKey == $"refund:{holdId}"));
    }

    [DisposablePostgresFact]
    public async Task OpposingCaptures_LockWalletsInConsistentOrder()
    {
        var (options, first, firstHold) = await Setup();
        var (unused, second, secondHold) = await Setup();
        var firstRequest = new CaptureHoldCommand("FORFEIT", null, [new("SELLER_WALLET", second, 50)])
            { HoldId = firstHold, IdempotencyKey = $"capture:{firstHold}" };
        var secondRequest = new CaptureHoldCommand("FORFEIT", null, [new("SELLER_WALLET", first, 50)])
            { HoldId = secondHold, IdempotencyKey = $"capture:{secondHold}" };
        await using var a = new WalletDbContext(options);
        await using var b = new WalletDbContext(options);
        await Task.WhenAll(new CaptureHoldCommandHandler(a).Handle(firstRequest, default),
            new CaptureHoldCommandHandler(b).Handle(secondRequest, default)).WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = new WalletDbContext(options);
        var wallets = await verify.Wallets.Where(w => w.UserId == first || w.UserId == second).ToArrayAsync();
        Assert.All(wallets, wallet => { Assert.Equal(100, wallet.Balance); Assert.Equal(0, wallet.LockedBalance); });
    }

    [DisposablePostgresFact]
    public async Task InsufficientEscrowAndKeyReuse_AreRejectedWithoutCredit()
    {
        var (options, buyer, holdId) = await Setup();
        var order = Guid.NewGuid();
        var request = new CaptureHoldCommand("ORDER_PAYMENT", order, [new("SYSTEM_ESCROW", null, 50)])
            { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" };
        await using (var context = new WalletDbContext(options))
            await new CaptureHoldCommandHandler(context).Handle(request, default);
        await using (var context = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                new RefundCapturedHoldCommandHandler(context).Handle(new("SELLER_REJECTED", order)
                    { HoldId = holdId, IdempotencyKey = request.IdempotencyKey }, default));
            Assert.Equal("IDEMPOTENCY_CONFLICT", error.ErrorCode);
        }
        await using (var context = new WalletDbContext(options))
        {
            var escrow = await context.AuctionDepositEscrows.SingleAsync(e => e.HoldId == holdId);
            escrow.RemainingAmount = 20;
            await context.SaveChangesAsync();
        }
        await using (var context = new WalletDbContext(options))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
                new RefundCapturedHoldCommandHandler(context).Handle(new("SELLER_REJECTED", order)
                    { HoldId = holdId, IdempotencyKey = $"refund:{holdId}" }, default));
            Assert.Equal("ESCROW_INSUFFICIENT", error.ErrorCode);
        }
        await using var verify = new WalletDbContext(options);
        Assert.Equal(50, (await verify.Wallets.SingleAsync(w => w.UserId == buyer)).Balance);
        Assert.Equal(AuctionHoldStatus.Captured, (await verify.AuctionHolds.SingleAsync(h => h.Id == holdId)).Status);
    }

    [DisposablePostgresFact]
    public async Task DatabaseFailureAfterBalanceMutation_RollsBackEntireCapture()
    {
        var (options, buyer, holdId) = await Setup();
        var seller = Guid.NewGuid();
        await using (var setup = new WalletDbContext(options))
        {
            setup.Wallets.Add(new WalletAccount
            {
                Id = Guid.NewGuid(), UserId = seller, Balance = 9999999999999999m, Currency = CurrencyCode.Vnd
            });
            await setup.SaveChangesAsync();
        }
        await using (var capture = new WalletDbContext(options))
            await Assert.ThrowsAsync<DbUpdateException>(() => new CaptureHoldCommandHandler(capture).Handle(
                new CaptureHoldCommand("FORFEIT", null, [new("SELLER_WALLET", seller, 50)])
                { HoldId = holdId, IdempotencyKey = $"capture:{holdId}" }, default));
        await using var verify = new WalletDbContext(options);
        var buyerWallet = await verify.Wallets.SingleAsync(w => w.UserId == buyer);
        Assert.Equal(100, buyerWallet.Balance);
        Assert.Equal(50, buyerWallet.LockedBalance);
        Assert.Equal(9999999999999999m, (await verify.Wallets.SingleAsync(w => w.UserId == seller)).Balance);
        Assert.Equal(AuctionHoldStatus.Held, (await verify.AuctionHolds.SingleAsync(h => h.Id == holdId)).Status);
        Assert.Equal(0, await verify.AuctionHoldCaptures.CountAsync(c => c.HoldId == holdId));
        Assert.Equal(0, await verify.WalletTransactions.CountAsync(t => t.ReferenceId == holdId));
    }

    private static async Task<bool> Attempt<T>(Func<Task<T>> action)
    {
        try { await action(); return true; }
        catch (BusinessRuleValidationException) { return false; }
    }

    private static async Task<(DbContextOptions<WalletDbContext>, Guid, Guid)> Setup()
    {
        var connectionString = Environment.GetEnvironmentVariable("WALLET_STRIPE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Disposable PostgreSQL test database is required.");
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseNpgsql(connectionString).Options;
        var user = Guid.NewGuid();
        await using var context = new WalletDbContext(options);
        await context.Database.MigrateAsync();
        context.Wallets.Add(new WalletAccount { Id = Guid.NewGuid(), UserId = user, Balance = 100, Currency = CurrencyCode.Vnd });
        await context.SaveChangesAsync();
        var hold = await new CreateHoldCommandHandler(context).Handle(new CreateHoldCommand(user, "AUCTION_DEPOSIT",
            Guid.NewGuid().ToString(), 50, "VND", null) { IdempotencyKey = $"hold:{user}" }, default);
        return (options, user, hold.Hold.HoldId);
    }
}
