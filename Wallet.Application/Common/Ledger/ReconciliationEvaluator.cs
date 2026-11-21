namespace Wallet.Application.Common.Ledger;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

public sealed record ReconciliationRequest(DateTimeOffset? From, DateTimeOffset? To, IReadOnlyCollection<string>? Invariants);

/// <summary>
/// Checks the invariants I1 to I12 and stores what is wrong as findings. It only reads money data: it never changes a balance, and
/// it never reports a finding for something it could not read (that makes the run Partial instead).
/// </summary>
public sealed class ReconciliationEvaluator
{
    public static readonly string[] AllInvariants = { "I1", "I2", "I3", "I4", "I5", "I6", "I7", "I8", "I9", "I10", "I11", "I12" };

    /// <summary>Data this recent may still be in flight, so it is not judged yet.</summary>
    public static readonly TimeSpan SettleBuffer = TimeSpan.FromSeconds(30);

    private const decimal Tolerance = 0.01m;
    private readonly IWalletDbContext _context;
    private readonly IReconciliationSourceClient _sources;

    public ReconciliationEvaluator(IWalletDbContext context, IReconciliationSourceClient sources)
    {
        _context = context;
        _sources = sources;
    }

    public async Task<ReconciliationRun> RunAsync(ReconciliationRequest request, Guid? createdBy, CancellationToken cancellationToken)
    {
        var wanted = (request.Invariants is { Count: > 0 } ? request.Invariants : AllInvariants)
            .Select(code => code.Trim().ToUpperInvariant()).ToHashSet();
        var run = new ReconciliationRun
        {
            CreatedBy = createdBy,
            FromDate = request.From,
            ToDate = request.To,
            Invariants = request.Invariants is { Count: > 0 } ? string.Join(',', wanted.OrderBy(code => code, StringComparer.Ordinal)) : null
        };

        var settings = await _context.LedgerSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var skipped = new List<string>();
        var unavailable = new List<string>();
        var findings = new List<ReconciliationFinding>();

        // One consistent snapshot for everything that reads the wallet's own tables.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? snapshot = null;
        if (_context.Database.IsRelational() && _context.Database.CurrentTransaction is null)
        {
            snapshot = await _context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        }

        try
        {
            var cutoff = DateTimeOffset.UtcNow - SettleBuffer;

            if (wanted.Overlaps(new[] { "I1", "I2", "I3", "I4", "I5" }))
            {
                IReadOnlyList<OrderRecon>? orders = null;
                try
                {
                    orders = await _sources.GetOrdersAsync(request.From, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unavailable.Add("commerce-service");
                    skipped.Add($"I1-I5: commerce-service could not be read ({Short(ex.Message)})");
                }

                if (orders is not null)
                {
                    orders = orders.Where(order => order.UpdatedAt <= cutoff && (!request.To.HasValue || order.UpdatedAt <= request.To.Value)).ToList();
                    if (wanted.Contains("I1")) CheckI1(orders, findings);
                    if (wanted.Overlaps(new[] { "I2", "I3", "I4", "I5" })) await CheckOrderMoneyAsync(orders, wanted, findings, cancellationToken);
                }
            }

            if (wanted.Contains("I6"))
            {
                try
                {
                    var deposits = await _sources.GetDepositsAsync(request.From, cancellationToken);
                    await CheckI6Async(deposits.Where(d => d.UpdatedAt <= cutoff).ToList(), findings, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unavailable.Add("auction-service");
                    skipped.Add($"I6: auction-service could not be read ({Short(ex.Message)})");
                }
            }

            if (wanted.Contains("I7")) await CheckI7Async(cutoff, findings, cancellationToken);
            if (wanted.Contains("I8")) await CheckI8Async(findings, cancellationToken);
            if (wanted.Contains("I9")) await CheckI9Async(findings, cancellationToken);

            var ledgerOn = settings is { Enabled: true };
            if (wanted.Overlaps(new[] { "I10", "I11", "I12" }))
            {
                if (settings is null || (!settings.Enabled && settings.ShadowEpoch == 0))
                {
                    skipped.Add("I10, I11, I12: the shadow ledger has not been switched on");
                }
                else
                {
                    if (wanted.Contains("I10")) await CheckI10Async(findings, cancellationToken);
                    if (wanted.Contains("I11")) await CheckI11Async(cutoff, findings, cancellationToken);
                    if (wanted.Contains("I12")) await CheckI12Async(cutoff, settings!, findings, cancellationToken);
                    if (!ledgerOn) skipped.Add("The shadow ledger is currently off: only what was recorded while it was on was checked");
                }
            }
        }
        finally
        {
            if (snapshot is not null) await snapshot.DisposeAsync();
        }

        run.UnavailableSources = unavailable.Count > 0 ? string.Join(',', unavailable) : null;
        run.SkippedNote = skipped.Count > 0 ? Truncate(string.Join("; ", skipped), 480) : null;
        run.Status = unavailable.Count > 0 ? ReconciliationRunStatus.Partial : ReconciliationRunStatus.Completed;

        // A problem that was already found is not stored again; the existing finding keeps its status and note.
        var keys = findings.Select(finding => finding.DedupKey).Distinct().ToList();
        var known = (await _context.ReconciliationFindings.Where(finding => keys.Contains(finding.DedupKey)).Select(finding => finding.DedupKey).ToListAsync(cancellationToken)).ToHashSet();
        var fresh = new List<ReconciliationFinding>();
        foreach (var finding in findings)
        {
            if (known.Add(finding.DedupKey))
            {
                finding.RunId = run.Id;
                fresh.Add(finding);
            }
        }

        run.FindingCount = fresh.Count;
        _context.ReconciliationRuns.Add(run);
        _context.ReconciliationFindings.AddRange(fresh);
        await _context.SaveChangesAsync(cancellationToken);
        return run;
    }

    // ---- I1: the sub-order money identity (Commerce only)
    private static void CheckI1(IReadOnlyList<OrderRecon> orders, List<ReconciliationFinding> findings)
    {
        foreach (var order in orders)
        {
            foreach (var sub in order.SubOrders)
            {
                var lhs = sub.SellerReceiveAmount + (sub.PlatformFeeAmount - sub.SystemDiscountAmount) + sub.ShippingFeeAmount;
                if (Math.Abs(lhs - sub.TotalAmount) > Tolerance)
                {
                    findings.Add(Finding("I1", "I1_FORMULA_MISMATCH", "High", sub.SubOrderId.ToString(), "commerce-service",
                        sub.TotalAmount, lhs, "SellerReceive + (PlatformFee - SystemDiscount) + Shipping does not equal Total."));
                }
            }
        }
    }

    // ---- I2 to I5: order payments, escrow, settlement and refunds (Commerce against Wallet)
    private async Task CheckOrderMoneyAsync(IReadOnlyList<OrderRecon> orders, HashSet<string> wanted, List<ReconciliationFinding> findings, CancellationToken ct)
    {
        var paidStatuses = new[] { "Paid", "Completed", "Delivered" };
        var walletOrders = orders
            .Where(order => paidStatuses.Contains(order.Status) && order.SubOrders.Count > 0 && order.SubOrders.All(sub => sub.PaymentMethod == "Wallet"))
            .ToList();
        var orderIds = walletOrders.Select(order => order.OrderId).ToList();
        var subOrderIds = walletOrders.SelectMany(order => order.SubOrders.Select(sub => sub.SubOrderId)).ToList();
        var refundTxIds = walletOrders.SelectMany(order => order.Refunds.SelectMany(r => new[] { r.RefundTransactionId, r.WalletTransactionId })).Where(id => id.HasValue).Select(id => id!.Value).ToList();

        var references = orderIds.Concat(subOrderIds).ToList();
        var txs = new List<WalletTransaction>();
        foreach (var chunk in references.Chunk(1000))
        {
            var ids = chunk.Select(id => (Guid?)id).ToList();
            txs.AddRange(await _context.WalletTransactions.AsNoTracking()
                .Where(tx => tx.Status == TransactionStatus.Success && tx.ReferenceId != null && ids.Contains(tx.ReferenceId)).ToListAsync(ct));
        }

        var byRef = txs.GroupBy(tx => tx.ReferenceId!.Value).ToDictionary(group => group.Key, group => group.ToList());
        var escrows = (await _context.OrderEscrows.AsNoTracking().Where(escrow => orderIds.Contains(escrow.OrderId)).ToListAsync(ct)).ToDictionary(escrow => escrow.OrderId);

        foreach (var order in walletOrders)
        {
            var orderTxs = byRef.TryGetValue(order.OrderId, out var list) ? list : new List<WalletTransaction>();
            var debits = orderTxs.Where(tx => tx.Type == WalletTransactionType.PurchaseDebit).ToList();

            if (wanted.Contains("I2"))
            {
                if (debits.Count == 0)
                {
                    findings.Add(Finding("I2", "I2_MISSING_WALLET_DEBIT", "High", order.OrderId.ToString(), "wallet-service", order.TotalAmount, 0m, "A wallet-paid order has no purchase debit."));
                }
                else if (debits.Count > 1)
                {
                    findings.Add(Finding("I2", "I2_DUPLICATE_DEBIT", "High", order.OrderId.ToString(), "wallet-service", 1, debits.Count, "A wallet-paid order was debited more than once."));
                }
                else
                {
                    var net = debits[0].Amount
                        + orderTxs.Where(tx => tx.Type == WalletTransactionType.OrderPaymentAdjustmentDebit).Sum(tx => tx.Amount)
                        - orderTxs.Where(tx => tx.Type == WalletTransactionType.OrderPaymentAdjustmentRefund).Sum(tx => tx.Amount);
                    if (Math.Abs(net - order.TotalAmount) > Tolerance)
                    {
                        findings.Add(Finding("I2", "I2_AMOUNT_MISMATCH", "High", order.OrderId.ToString(), "wallet-service", order.TotalAmount, net, "The wallet debit does not match the order total."));
                    }
                }
            }

            if (wanted.Contains("I3") && debits.Count == 1)
            {
                if (!escrows.TryGetValue(order.OrderId, out var escrow))
                {
                    findings.Add(Finding("I3", "I3_MISSING_ESCROW", "High", order.OrderId.ToString(), "wallet-service", debits[0].Amount, 0m, "A wallet payment has no escrow record."));
                }
                else
                {
                    var released = order.SubOrders.Sum(sub => byRef.TryGetValue(sub.SubOrderId, out var subTxs)
                        ? subTxs.Where(tx => tx.Type == WalletTransactionType.EscrowRelease).Sum(tx => tx.Amount) : 0m);
                    var returned = orderTxs.Where(tx => tx.Type is WalletTransactionType.RefundCredit or WalletTransactionType.EscrowCancellationRefund).Sum(tx => tx.Amount);
                    var adjustments = orderTxs.Where(tx => tx.Type == WalletTransactionType.OrderPaymentAdjustmentDebit).Sum(tx => tx.Amount)
                        - orderTxs.Where(tx => tx.Type == WalletTransactionType.OrderPaymentAdjustmentRefund).Sum(tx => tx.Amount);
                    if (Math.Abs(escrow.OriginalAmount - (debits[0].Amount + adjustments)) > Tolerance)
                    {
                        findings.Add(Finding("I3", "I3_ORIGINAL_MISMATCH", "High", order.OrderId.ToString(), "wallet-service", debits[0].Amount + adjustments, escrow.OriginalAmount, "Escrow original amount does not match what the buyer paid."));
                    }

                    var expectedRemaining = escrow.OriginalAmount - released - returned;
                    if (Math.Abs(escrow.RemainingAmount - expectedRemaining) > Tolerance)
                    {
                        findings.Add(Finding("I3", "I3_REMAINING_MISMATCH", "High", order.OrderId.ToString(), "wallet-service", expectedRemaining, escrow.RemainingAmount, "Escrow remaining amount does not equal original minus payouts and refunds."));
                    }
                }
            }

            if (wanted.Contains("I4"))
            {
                foreach (var sub in order.SubOrders.Where(sub => sub.Status == "Completed" || sub.IsEscrowSettled))
                {
                    var releases = byRef.TryGetValue(sub.SubOrderId, out var subTxs)
                        ? subTxs.Where(tx => tx.Type == WalletTransactionType.EscrowRelease).ToList() : new List<WalletTransaction>();
                    if (releases.Count == 0)
                    {
                        findings.Add(Finding("I4", "I4_MISSING_RELEASE", "High", sub.SubOrderId.ToString(), "wallet-service", sub.SellerReceiveAmount, 0m, "A completed sub-order has no seller payout."));
                    }
                    else if (Math.Abs(releases.Sum(tx => tx.Amount) - sub.SellerReceiveAmount) > Tolerance)
                    {
                        findings.Add(Finding("I4", "I4_AMOUNT_MISMATCH", "High", sub.SubOrderId.ToString(), "wallet-service", sub.SellerReceiveAmount, releases.Sum(tx => tx.Amount), "The seller payout does not match the amount due."));
                    }
                }
            }
        }

        if (wanted.Contains("I5"))
        {
            var refundTxs = refundTxIds.Count == 0 ? new Dictionary<Guid, WalletTransaction>()
                : (await _context.WalletTransactions.AsNoTracking().Where(tx => refundTxIds.Contains(tx.Id)).ToListAsync(ct)).ToDictionary(tx => tx.Id);
            foreach (var order in walletOrders)
            {
                foreach (var refund in order.Refunds.Where(refund => refund.Status == "Completed"))
                {
                    var txId = refund.RefundTransactionId ?? refund.WalletTransactionId;
                    if (txId is null || !refundTxs.TryGetValue(txId.Value, out var tx) || tx.Status != TransactionStatus.Success)
                    {
                        findings.Add(Finding("I5", "I5_MISSING_REFUND_CREDIT", "High", refund.RefundId.ToString(), "wallet-service", refund.Amount, 0m, "A completed refund has no successful wallet credit."));
                    }
                    else if (Math.Abs(tx.Amount - refund.Amount) > Tolerance)
                    {
                        findings.Add(Finding("I5", "I5_AMOUNT_MISMATCH", "High", refund.RefundId.ToString(), "wallet-service", refund.Amount, tx.Amount, "The refund credit does not match the refund amount."));
                    }
                }
            }
        }
    }

    // ---- I6: auction deposits against wallet holds
    private async Task CheckI6Async(IReadOnlyList<DepositRecon> deposits, List<ReconciliationFinding> findings, CancellationToken ct)
    {
        var holdIds = deposits.Select(d => Guid.TryParse(d.HoldId, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        var holds = holdIds.Count == 0 ? new Dictionary<Guid, AuctionHold>()
            : (await _context.AuctionHolds.AsNoTracking().Where(hold => holdIds.Contains(hold.Id)).ToListAsync(ct)).ToDictionary(hold => hold.Id);

        foreach (var deposit in deposits.Where(d => d.Status is "Authorized" or "Captured" or "Released"))
        {
            if (!Guid.TryParse(deposit.HoldId, out var holdId) || !holds.TryGetValue(holdId, out var hold))
            {
                findings.Add(Finding("I6", "I6_MISSING_HOLD", "High", deposit.DepositId.ToString(), "wallet-service", deposit.Amount, 0m, "A deposit has no matching wallet hold."));
                continue;
            }

            if (Math.Abs(hold.Amount - deposit.Amount) > Tolerance)
            {
                findings.Add(Finding("I6", "I6_AMOUNT_MISMATCH", "High", deposit.DepositId.ToString(), "wallet-service", deposit.Amount, hold.Amount, "The hold amount does not match the deposit."));
            }

            var expected = deposit.Status switch
            {
                "Authorized" => new[] { AuctionHoldStatus.Held },
                "Captured" => new[] { AuctionHoldStatus.Captured, AuctionHoldStatus.Refunded },
                _ => new[] { AuctionHoldStatus.Released }
            };
            if (!expected.Contains(hold.Status))
            {
                findings.Add(Finding("I6", "I6_STATUS_MISMATCH", "High", deposit.DepositId.ToString(), "wallet-service", deposit.Status, hold.Status.ToString(), "The deposit and the wallet hold disagree on state."));
            }
        }
    }

    // ---- I7: a successful top-up has exactly one credit
    private async Task CheckI7Async(DateTimeOffset cutoff, List<ReconciliationFinding> findings, CancellationToken ct)
    {
        var topups = await _context.WalletTopups.AsNoTracking().Where(topup => topup.Status == TopupStatus.Success && topup.UpdatedAt <= cutoff).ToListAsync(ct);
        if (topups.Count == 0) return;
        var ids = topups.Select(topup => (Guid?)topup.Id).ToList();
        var counts = await _context.WalletTransactions.AsNoTracking()
            .Where(tx => tx.Status == TransactionStatus.Success && (tx.Type == WalletTransactionType.TopupStripe || tx.Type == WalletTransactionType.TopupVnpay) && tx.ReferenceId != null && ids.Contains(tx.ReferenceId))
            .GroupBy(tx => tx.ReferenceId!.Value).Select(group => new { Id = group.Key, Count = group.Count(), Sum = group.Sum(tx => tx.Amount) }).ToListAsync(ct);
        var map = counts.ToDictionary(item => item.Id);
        foreach (var topup in topups)
        {
            if (!map.TryGetValue(topup.Id, out var item))
                findings.Add(Finding("I7", "I7_MISSING_TOPUP_CREDIT", "Medium", topup.Id.ToString(), "wallet-service", topup.Amount, 0m, "A successful top-up has no wallet credit."));
            else if (item.Count > 1)
                findings.Add(Finding("I7", "I7_DUPLICATE_TOPUP_CREDIT", "High", topup.Id.ToString(), "wallet-service", 1, item.Count, "A top-up was credited more than once."));
            else if (Math.Abs(item.Sum - topup.Amount) > Tolerance)
                findings.Add(Finding("I7", "I7_AMOUNT_MISMATCH", "Medium", topup.Id.ToString(), "wallet-service", topup.Amount, item.Sum, "The top-up credit does not match the top-up amount."));
        }
    }

    // ---- I8: the balance chain (a supporting check: it cannot prove an order, only notice a break)
    private async Task CheckI8Async(List<ReconciliationFinding> findings, CancellationToken ct)
    {
        var wallets = await _context.Wallets.AsNoTracking().Select(wallet => new { wallet.Id, wallet.Balance }).ToListAsync(ct);
        foreach (var wallet in wallets)
        {
            var chain = await _context.WalletTransactions.AsNoTracking()
                .Where(tx => tx.WalletId == wallet.Id && tx.Status == TransactionStatus.Success && tx.Type != WalletTransactionType.DirectGatewayPayment && tx.BalanceBefore != tx.BalanceAfter)
                .OrderBy(tx => tx.CreatedAt).ThenBy(tx => tx.Id)
                .Select(tx => new { tx.Id, tx.BalanceBefore, tx.BalanceAfter })
                .ToListAsync(ct);
            if (chain.Count == 0) continue;
            for (var i = 1; i < chain.Count; i++)
            {
                if (chain[i].BalanceBefore != chain[i - 1].BalanceAfter)
                {
                    findings.Add(Finding("I8", "I8_CHAIN_BREAK", "Medium", chain[i].Id.ToString(), "wallet-service", chain[i - 1].BalanceAfter, chain[i].BalanceBefore, "A transaction does not start from the balance the previous one ended with."));
                    break;
                }
            }

            if (chain[^1].BalanceAfter != wallet.Balance)
            {
                findings.Add(Finding("I8", "I8_LAST_BALANCE_MISMATCH", "High", wallet.Id.ToString(), "wallet-service", chain[^1].BalanceAfter, wallet.Balance, "The wallet balance differs from the balance after its last transaction."));
            }
        }
    }

    // ---- I9: locked balance equals active holds plus pending withdrawals
    private async Task CheckI9Async(List<ReconciliationFinding> findings, CancellationToken ct)
    {
        var wallets = await _context.Wallets.AsNoTracking().Select(wallet => new { wallet.Id, wallet.LockedBalance }).ToListAsync(ct);
        var holds = (await _context.AuctionHolds.AsNoTracking().Where(hold => hold.Status == AuctionHoldStatus.Held)
            .GroupBy(hold => hold.WalletId).Select(group => new { group.Key, Sum = group.Sum(hold => hold.Amount) }).ToListAsync(ct)).ToDictionary(item => item.Key, item => item.Sum);
        var withdrawals = (await _context.WithdrawalRequests.AsNoTracking()
            .Where(w => w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Approved || w.Status == WithdrawalStatus.Processing)
            .GroupBy(w => w.WalletId).Select(group => new { group.Key, Sum = group.Sum(w => w.Amount) }).ToListAsync(ct)).ToDictionary(item => item.Key, item => item.Sum);

        foreach (var wallet in wallets)
        {
            var expected = (holds.TryGetValue(wallet.Id, out var h) ? h : 0m) + (withdrawals.TryGetValue(wallet.Id, out var w) ? w : 0m);
            if (Math.Abs(wallet.LockedBalance - expected) > Tolerance)
            {
                findings.Add(Finding("I9", "I9_LOCKED_MISMATCH", "High", wallet.Id.ToString(), "wallet-service", expected, wallet.LockedBalance, "Locked balance does not equal active holds plus pending withdrawals."));
            }
        }
    }

    // ---- I10: the shadow ledger agrees with the wallet. (a) entries balance. (b) per wallet, over one unbroken run of one epoch.
    private async Task CheckI10Async(List<ReconciliationFinding> findings, CancellationToken ct)
    {
        var unbalanced = await _context.JournalLines.AsNoTracking().GroupBy(line => line.EntryId)
            .Where(group => group.Sum(line => line.Debit) != group.Sum(line => line.Credit)).Select(group => group.Key).Take(200).ToListAsync(ct);
        foreach (var entryId in unbalanced)
        {
            findings.Add(Finding("I10", "I10_ENTRY_UNBALANCED", "Critical", entryId.ToString(), "wallet-service", "debit = credit", "debit != credit", "A journal entry does not balance."));
        }

        foreach (var result in await EvaluateWalletLedgersAsync(ct))
        {
            if (result.Result == "Mismatched")
            {
                findings.Add(Finding("I10", "I10_WALLET_LEDGER_MISMATCH", "High", result.WalletId.ToString(), "wallet-service", result.Expected, result.Actual, "The shadow ledger does not add up to the wallet balance over an unbroken run."));
            }
        }
    }

    // ---- I11: events past the buffer that are still not posted
    private async Task CheckI11Async(DateTimeOffset cutoff, List<ReconciliationFinding> findings, CancellationToken ct)
    {
        var stuck = await _context.LedgerPostingPending.AsNoTracking()
            .Where(row => row.Status != LedgerPostingStatus.Posted && row.CreatedAt <= cutoff.AddMinutes(-1))
            .OrderBy(row => row.CreatedAt).Take(500).ToListAsync(ct);
        foreach (var row in stuck)
        {
            findings.Add(Finding("I11", row.Status == LedgerPostingStatus.Failed ? "I11_POSTING_FAILED" : "I11_POSTING_OVERDUE", "High",
                row.Id.ToString(), "wallet-service", "Posted", row.Status.ToString(), $"A balance event of wallet {row.WalletId} was not posted to the journal in time."));
        }
    }

    // ---- I12: every balance-changing successful transaction in scope has a queue row (supporting, with stated limits)
    private async Task CheckI12Async(DateTimeOffset cutoff, LedgerSettings settings, List<ReconciliationFinding> findings, CancellationToken ct)
    {
        // The earliest moment the ledger may have been on. Transactions close to it (before or after) are "not conclusive", not findings.
        var firstOn = await _context.LedgerPostingPending.AsNoTracking().Where(row => row.IsEpochOpening).MinAsync(row => (DateTimeOffset?)row.CreatedAt, ct);
        if (firstOn is null) return;
        var margin = TimeSpan.FromMinutes(2);
        var from = firstOn.Value + margin;
        var covered = (await _context.LedgerPostingPending.AsNoTracking().Where(row => row.RelatedTransactionIds != null).Select(row => row.RelatedTransactionIds!).ToListAsync(ct))
            .SelectMany(text => text.Split(',', StringSplitOptions.RemoveEmptyEntries)).Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty).ToHashSet();

        var candidates = await _context.WalletTransactions.AsNoTracking()
            .Where(tx => tx.Status == TransactionStatus.Success && tx.Type != WalletTransactionType.DirectGatewayPayment && tx.BalanceBefore != tx.BalanceAfter
                && tx.UpdatedAt >= from && tx.UpdatedAt <= cutoff)
            .OrderBy(tx => tx.UpdatedAt).Take(2000).ToListAsync(ct);
        foreach (var tx in candidates.Where(tx => !covered.Contains(tx.Id)))
        {
            // A wallet only opens its epoch at its own first event after switch-on, so a gap before its opening row is history, not a miss.
            var opening = await _context.LedgerPostingPending.AsNoTracking().Where(row => row.WalletId == tx.WalletId && row.IsEpochOpening).MinAsync(row => (DateTimeOffset?)row.CreatedAt, ct);
            if (opening is null || tx.UpdatedAt < opening.Value + margin) continue;
            findings.Add(Finding("I12", "I12_MISSING_POSTING_ROW", "Medium", tx.Id.ToString(), "wallet-service", "queue row", "none", "A successful balance-changing transaction has no ledger queue row."));
        }
    }

    public sealed record WalletLedgerResult(Guid WalletId, string Result, string? Reason, int Epoch, decimal Expected, decimal Actual);

    /// <summary>Evaluates I10(b) for every wallet the ledger follows, in the caller's snapshot. Matched, Mismatched, or Unchecked with a reason: never "Matched" without proof.</summary>
    public async Task<IReadOnlyList<WalletLedgerResult>> EvaluateWalletLedgersAsync(CancellationToken ct)
    {
        var results = new List<WalletLedgerResult>();
        var wallets = await _context.Wallets.AsNoTracking().Where(wallet => wallet.LedgerEpoch > 0).Select(wallet => new { wallet.Id, wallet.LedgerEpoch, wallet.Balance }).ToListAsync(ct);
        foreach (var wallet in wallets)
        {
            var rows = await _context.LedgerPostingPending.AsNoTracking()
                .Where(row => row.WalletId == wallet.Id && row.LedgerEpoch == wallet.LedgerEpoch).OrderBy(row => row.LedgerSeq).ToListAsync(ct);
            if (rows.Count == 0 || !rows[0].IsEpochOpening || rows[0].LedgerSeq != 1)
            {
                results.Add(new WalletLedgerResult(wallet.Id, "Unchecked", "The epoch has no opening event.", wallet.LedgerEpoch, 0m, 0m));
                continue;
            }

            // The unbroken run: consecutive sequence numbers that are already posted.
            var run = new List<LedgerPostingPending>();
            foreach (var row in rows)
            {
                if (row.LedgerSeq != run.Count + 1 || row.Status != LedgerPostingStatus.Posted) break;
                run.Add(row);
            }

            if (run.Count == 0)
            {
                results.Add(new WalletLedgerResult(wallet.Id, "Unchecked", "The opening event is still being posted.", wallet.LedgerEpoch, 0m, 0m));
                continue;
            }

            if (run.Count < rows.Count && rows[run.Count].LedgerSeq != run.Count + 1)
            {
                results.Add(new WalletLedgerResult(wallet.Id, "Unchecked", "The sequence has a gap, so the order cannot be proven.", wallet.LedgerEpoch, 0m, 0m));
                continue;
            }

            var pendingIds = run.Select(row => row.Id).ToList();
            var lines = await (from entry in _context.JournalEntries.AsNoTracking()
                               join line in _context.JournalLines.AsNoTracking() on entry.Id equals line.EntryId
                               where entry.PendingId != null && pendingIds.Contains(entry.PendingId.Value) && line.AccountCode == LedgerPostingRules.Wallet
                               select new { line.Debit, line.Credit }).ToListAsync(ct);
            var expected = lines.Sum(line => line.Credit - line.Debit);
            var actual = run[^1].WalletBalanceAfter;
            results.Add(Math.Abs(expected - actual) <= Tolerance
                ? new WalletLedgerResult(wallet.Id, "Matched", null, wallet.LedgerEpoch, expected, actual)
                : new WalletLedgerResult(wallet.Id, "Mismatched", null, wallet.LedgerEpoch, expected, actual));
        }

        return results;
    }

    private static ReconciliationFinding Finding(string invariant, string code, string severity, string reference, string source, object expected, object actual, string description) => new()
    {
        InvariantCode = invariant,
        Code = code,
        Severity = severity,
        ReferenceId = reference,
        Source = source,
        ExpectedValue = Convert.ToString(expected, System.Globalization.CultureInfo.InvariantCulture),
        ActualValue = Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture),
        Description = description,
        DedupKey = $"{invariant}|{reference}|{code}"
    };

    private static string Short(string text) => text.Length > 80 ? text[..80] : text;

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}
