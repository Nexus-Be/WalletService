namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Ledger;
using Wallet.Application.Features.Ledger.Commands.SetShadowLedger;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class ShadowLedgerTests
{
    private sealed class FakeSources : IReconciliationSourceClient
    {
        public List<OrderRecon> Orders { get; } = new();
        public bool CommerceDown { get; set; }

        public Task<IReadOnlyList<OrderRecon>> GetOrdersAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken) =>
            CommerceDown ? throw new InvalidOperationException("down") : Task.FromResult<IReadOnlyList<OrderRecon>>(Orders);

        public Task<IReadOnlyList<DepositRecon>> GetDepositsAsync(DateTimeOffset? updatedFrom, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DepositRecon>>(Array.Empty<DepositRecon>());
    }

    private static async Task<(TestWalletDbContext Db, WalletAccount Wallet)> NewAsync(bool ledgerOn, int epoch = 1, decimal balance = 100m)
    {
        var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        db.LedgerSettings.Add(new LedgerSettings { Id = 1, Enabled = ledgerOn, ShadowEpoch = ledgerOn ? epoch : 0, Version = ledgerOn ? 1 : 0 });
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Balance = balance, Currency = CurrencyCode.Vnd };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return (db, wallet);
    }

    private static async Task Credit(TestWalletDbContext db, WalletAccount wallet, decimal amount, WalletTransactionType type = WalletTransactionType.TopupStripe)
    {
        var before = wallet.Balance;
        wallet.Deposit(amount);
        db.WalletTransactions.Add(new WalletTransaction { WalletId = wallet.Id, Type = type, Amount = amount, BalanceBefore = before, BalanceAfter = wallet.Balance, Status = TransactionStatus.Success });
        await db.SaveChangesAsync();
    }

    private static async Task Debit(TestWalletDbContext db, WalletAccount wallet, decimal amount)
    {
        var before = wallet.Balance;
        wallet.Deduct(amount);
        db.WalletTransactions.Add(new WalletTransaction { WalletId = wallet.Id, Type = WalletTransactionType.PurchaseDebit, Amount = amount, BalanceBefore = before, BalanceAfter = wallet.Balance, Status = TransactionStatus.Success });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ledger_off_records_nothing_and_never_blocks_a_balance_change()
    {
        var (db, wallet) = await NewAsync(ledgerOn: false);
        await Credit(db, wallet, 50m);
        Assert.Empty(await db.LedgerPostingPending.ToListAsync());
        Assert.Equal(150m, wallet.Balance);
    }

    [Fact]
    public async Task Ledger_on_opens_an_epoch_then_numbers_events_in_order_with_the_balance_after_each()
    {
        var (db, wallet) = await NewAsync(ledgerOn: true, epoch: 2);
        await Credit(db, wallet, 50m);
        await Debit(db, wallet, 30m);

        var rows = await db.LedgerPostingPending.OrderBy(row => row.LedgerSeq).ToListAsync();
        Assert.Equal(new long[] { 1, 2, 3 }, rows.Select(row => row.LedgerSeq).ToArray());
        Assert.True(rows[0].IsEpochOpening);
        Assert.Equal(100m, rows[0].OpeningBalance);
        Assert.Equal(new[] { 0m, 50m, -30m }, rows.Select(row => row.Delta).ToArray());
        Assert.Equal(new[] { 100m, 150m, 120m }, rows.Select(row => row.WalletBalanceAfter).ToArray());
        Assert.All(rows, row => Assert.Equal(2, row.LedgerEpoch));
        Assert.Equal(2, wallet.LedgerEpoch);
        Assert.Equal(3, wallet.LedgerSeq);
    }

    [Fact]
    public async Task A_balance_change_without_a_successful_transaction_is_refused_while_the_ledger_is_on()
    {
        var (db, wallet) = await NewAsync(ledgerOn: true);
        wallet.Deposit(10m);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_direct_gateway_payment_that_does_not_move_the_balance_is_not_a_ledger_event_and_is_not_refused()
    {
        var (db, wallet) = await NewAsync(ledgerOn: true);
        db.WalletTransactions.Add(new WalletTransaction { WalletId = wallet.Id, Type = WalletTransactionType.DirectGatewayPayment, Amount = 40m, BalanceBefore = 100m, BalanceAfter = 100m, Status = TransactionStatus.Success });
        await db.SaveChangesAsync();
        Assert.Empty(await db.LedgerPostingPending.ToListAsync());
    }

    [Fact]
    public async Task Posting_makes_balanced_entries_and_I10b_is_matched_only_with_proof()
    {
        var (db, wallet) = await NewAsync(ledgerOn: true);
        await Credit(db, wallet, 50m);
        await Debit(db, wallet, 30m);
        var evaluator = new ReconciliationEvaluator(db, new FakeSources());

        // Nothing is posted yet: the answer is "not checked", never "matched".
        var before = Assert.Single(await evaluator.EvaluateWalletLedgersAsync(default));
        Assert.Equal("Unchecked", before.Result);

        await new JournalPoster(db).PostPendingAsync(100, default);
        var after = Assert.Single(await evaluator.EvaluateWalletLedgersAsync(default));
        Assert.Equal("Matched", after.Result);

        var lines = await db.JournalLines.ToListAsync();
        Assert.Equal(lines.Sum(line => line.Debit), lines.Sum(line => line.Credit));
    }

    [Fact]
    public async Task A_deleted_entry_is_never_reported_as_matched()
    {
        var (db, wallet) = await NewAsync(ledgerOn: true);
        await Credit(db, wallet, 50m);
        await Debit(db, wallet, 30m);
        await new JournalPoster(db).PostPendingAsync(100, default);

        var victim = await db.JournalEntries.Where(entry => entry.Description == "TopupStripe").Include(entry => entry.Lines).SingleAsync();
        db.JournalLines.RemoveRange(victim.Lines);
        db.JournalEntries.Remove(victim);
        await db.SaveChangesAsync();

        var result = Assert.Single(await new ReconciliationEvaluator(db, new FakeSources()).EvaluateWalletLedgersAsync(default));
        Assert.NotEqual("Matched", result.Result);
        Assert.Equal("Mismatched", result.Result);
    }

    [Fact]
    public async Task A_gap_in_the_sequence_makes_the_wallet_unchecked_with_a_reason()
    {
        var (db, wallet) = await NewAsync(ledgerOn: true);
        await Credit(db, wallet, 50m);
        await Debit(db, wallet, 30m);
        await new JournalPoster(db).PostPendingAsync(100, default);
        db.LedgerPostingPending.Remove(await db.LedgerPostingPending.SingleAsync(row => row.LedgerSeq == 2));
        await db.SaveChangesAsync();

        var result = Assert.Single(await new ReconciliationEvaluator(db, new FakeSources()).EvaluateWalletLedgersAsync(default));
        Assert.Equal("Unchecked", result.Result);
        Assert.Contains("gap", result.Reason);
    }

    [Fact]
    public async Task Switching_on_twice_changes_the_epoch_once_a_stale_version_is_refused_and_each_new_epoch_starts_from_the_current_balance()
    {
        var (db, wallet) = await NewAsync(ledgerOn: false);
        var handler = new SetShadowLedgerCommandHandler(db);
        var admin = Guid.NewGuid();

        var first = await handler.Handle(new SetShadowLedgerCommand(true, 0) { AdminUserId = admin }, default);
        Assert.True(first.Changed);
        Assert.Equal(1, first.ShadowEpoch);

        var again = await handler.Handle(new SetShadowLedgerCommand(true, first.Version) { AdminUserId = admin }, default);
        Assert.False(again.Changed);
        Assert.Equal(1, again.ShadowEpoch);

        var stale = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new SetShadowLedgerCommand(false, 0) { AdminUserId = admin }, default));
        Assert.Equal(409, stale.StatusCode);

        await Credit(db, wallet, 25m);                                   // epoch 1 opens at 100
        await handler.Handle(new SetShadowLedgerCommand(false, again.Version) { AdminUserId = admin }, default);
        await Credit(db, wallet, 5m);                                    // ledger off: nothing recorded
        var on2 = await handler.Handle(new SetShadowLedgerCommand(true, 2) { AdminUserId = admin }, default);
        Assert.Equal(2, on2.ShadowEpoch);
        await Credit(db, wallet, 10m);                                   // epoch 2 opens at 130

        var opening = await db.LedgerPostingPending.Where(row => row.LedgerEpoch == 2 && row.IsEpochOpening).SingleAsync();
        Assert.Equal(130m, opening.OpeningBalance);
        Assert.Equal(2, wallet.LedgerEpoch);
        Assert.Equal(2, wallet.LedgerSeq);
    }

    [Fact]
    public async Task Reconciliation_reports_a_broken_order_identity_once_and_skips_ledger_checks_when_the_ledger_was_never_on()
    {
        var (db, _) = await NewAsync(ledgerOn: false);
        var sources = new FakeSources();
        var sub = new SubOrderRecon(Guid.NewGuid(), "Completed", "Cod", 100m, 0m, 0m, 0m, 10m, 120m, 90m, 10m, false, null); // 90 + 10 + 10 != 120
        sources.Orders.Add(new OrderRecon(Guid.NewGuid(), Guid.NewGuid(), "Completed", 120m, DateTimeOffset.UtcNow.AddMinutes(-10), new[] { sub }, Array.Empty<RefundRecon>()));
        var evaluator = new ReconciliationEvaluator(db, sources);

        var run = await evaluator.RunAsync(new ReconciliationRequest(null, null, new[] { "I1", "I10", "I11" }), Guid.NewGuid(), default);
        Assert.Equal(1, run.FindingCount);
        Assert.Equal(ReconciliationRunStatus.Completed, run.Status);
        Assert.Contains("I10", run.SkippedNote);
        Assert.Equal("I1_FORMULA_MISMATCH", (await db.ReconciliationFindings.SingleAsync()).Code);

        var second = await evaluator.RunAsync(new ReconciliationRequest(null, null, new[] { "I1" }), Guid.NewGuid(), default);
        Assert.Equal(0, second.FindingCount);
        Assert.Equal(1, await db.ReconciliationFindings.CountAsync());
    }

    [Fact]
    public async Task An_unreadable_source_makes_the_run_Partial_and_creates_no_false_finding()
    {
        var (db, _) = await NewAsync(ledgerOn: false);
        var sources = new FakeSources { CommerceDown = true };
        var run = await new ReconciliationEvaluator(db, sources).RunAsync(new ReconciliationRequest(null, null, new[] { "I1", "I2" }), null, default);
        Assert.Equal(ReconciliationRunStatus.Partial, run.Status);
        Assert.Equal("commerce-service", run.UnavailableSources);
        Assert.Equal(0, run.FindingCount);
    }

    [Fact]
    public async Task A_wallet_paid_order_without_a_debit_is_reported_and_the_run_changes_no_balance()
    {
        var (db, wallet) = await NewAsync(ledgerOn: false);
        var sources = new FakeSources();
        var sub = new SubOrderRecon(Guid.NewGuid(), "Paid", "Wallet", 100m, 0m, 0m, 0m, 10m, 110m, 90m, 20m, false, DateTimeOffset.UtcNow);
        sources.Orders.Add(new OrderRecon(Guid.NewGuid(), Guid.NewGuid(), "Paid", 110m, DateTimeOffset.UtcNow.AddMinutes(-10), new[] { sub }, Array.Empty<RefundRecon>()));
        var balanceBefore = wallet.Balance;

        var run = await new ReconciliationEvaluator(db, sources).RunAsync(new ReconciliationRequest(null, null, new[] { "I2" }), null, default);

        Assert.Equal("I2_MISSING_WALLET_DEBIT", (await db.ReconciliationFindings.SingleAsync()).Code);
        Assert.Equal(balanceBefore, (await db.Wallets.SingleAsync()).Balance);
        Assert.Equal(1, run.FindingCount);
    }

    [Fact]
    public async Task A_completed_refund_is_matched_to_its_credit_through_the_sub_order_even_without_a_stored_link()
    {
        var (db, wallet) = await NewAsync(ledgerOn: false);
        var orderId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        db.WalletTransactions.Add(new WalletTransaction { WalletId = wallet.Id, Type = WalletTransactionType.PurchaseDebit, Amount = 110m, BalanceBefore = 210m, BalanceAfter = 100m, Status = TransactionStatus.Success, ReferenceId = orderId });
        db.WalletTransactions.Add(new WalletTransaction { WalletId = wallet.Id, Type = WalletTransactionType.RefundCredit, Amount = 110m, BalanceBefore = 100m, BalanceAfter = 210m, Status = TransactionStatus.Success, ReferenceId = subId });
        await db.SaveChangesAsync();

        var sources = new FakeSources();
        var sub = new SubOrderRecon(subId, "Paid", "Wallet", 100m, 0m, 0m, 0m, 10m, 110m, 90m, 20m, false, DateTimeOffset.UtcNow);
        var refund = new RefundRecon(Guid.NewGuid(), subId, orderId, 110m, "Completed", null, null);
        sources.Orders.Add(new OrderRecon(orderId, Guid.NewGuid(), "Paid", 110m, DateTimeOffset.UtcNow.AddMinutes(-10), new[] { sub }, new[] { refund }));

        var clean = await new ReconciliationEvaluator(db, sources).RunAsync(new ReconciliationRequest(null, null, new[] { "I5" }), null, default);
        Assert.Equal(0, clean.FindingCount);

        var credit = await db.WalletTransactions.SingleAsync(tx => tx.Type == WalletTransactionType.RefundCredit);
        credit.Amount = 90m;
        await db.SaveChangesAsync();
        var broken = await new ReconciliationEvaluator(db, sources).RunAsync(new ReconciliationRequest(null, null, new[] { "I5" }), null, default);
        Assert.Equal("I5_AMOUNT_MISMATCH", (await db.ReconciliationFindings.SingleAsync()).Code);
        Assert.Equal(1, broken.FindingCount);
    }
}
