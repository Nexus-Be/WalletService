namespace Wallet.Application.Features.RefundFund.Commands.PayRefundFundCase;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using global::Wallet.Application.Common;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class PayRefundFundCaseCommandHandler : IRequestHandler<PayRefundFundCaseCommand, RefundFundCaseResult>
{
    private const string NeedsFinance = "NeedsFinance";
    private static readonly WalletTransactionType[] RefundTypes =
    {
        WalletTransactionType.RefundCredit, WalletTransactionType.EscrowCancellationRefund, WalletTransactionType.RefundFundRefund
    };

    private readonly IWalletDbContext _context;
    private readonly IOptions<RefundFundOptions> _options;

    public PayRefundFundCaseCommandHandler(IWalletDbContext context, IOptions<RefundFundOptions> options)
    {
        _context = context;
        _options = options;
    }

    public async Task<RefundFundCaseResult> Handle(PayRefundFundCaseCommand request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
            throw new BusinessRuleValidationException("REFUND_FUND_DISABLED", "The refund fund is not enabled.", 503);

        var subOrderId = request.SubOrderId;
        if (subOrderId == Guid.Empty || request.ParentOrderId == Guid.Empty)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Order and sub-order IDs are required.", 400);

        var buyerId = Guid.Parse(request.BuyerId);
        var first = Guid.Parse(request.FirstApproverId);
        Guid? second = Guid.TryParse(request.SecondApproverId, out var parsedSecond) && parsedSecond != Guid.Empty ? parsedSecond : null;

        // One refund per sub-order: a repeated request returns the stored answer, a different one is a conflict.
        var existing = await _context.RefundFundPayouts.AsNoTracking().FirstOrDefaultAsync(item => item.SubOrderId == subOrderId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Amount != request.Amount || existing.ParentOrderId != request.ParentOrderId || existing.BuyerId != buyerId)
                throw new BusinessRuleValidationException("REFUND_FUND_CONFLICT", "This sub-order was already refunded from the fund with different details.", 409);
            var balance = await FundBalanceAsync(cancellationToken);
            return Paid(existing, balance, replayed: true);
        }

        if (request.Amount > options.SecondApprovalThreshold && (second is null || second == first))
            throw new BusinessRuleValidationException("SECOND_APPROVAL_REQUIRED", "A second admin, different from the first, must approve this amount.", 403);

        // The fund only pays what was already paid out: the sub-order must have a payout record, and the whole refund is what the buyer paid.
        var settlement = await _context.OrderSettlements.AsNoTracking().FirstOrDefaultAsync(item =>
            item.SubOrderId == subOrderId && item.ParentOrderId == request.ParentOrderId && item.BusinessType == "OrderPayout", cancellationToken)
            ?? throw new BusinessRuleValidationException("SUB_ORDER_NOT_SETTLED", "The sub-order has not been paid out; refund it from its escrow instead.", 409);
        if (request.Amount != settlement.OrderEscrowAmount)
            throw new BusinessRuleValidationException("PARTIAL_REFUND_NOT_SUPPORTED", "Only the whole amount the buyer paid for the sub-order can be refunded from the fund.", 400);

        // Reasons that stop the payment without moving money. They are answers, not errors: Commerce keeps the case in NeedsFinance.
        if (request.Amount > options.PerCaseCap)
            return Stopped("PER_CASE_CAP_EXCEEDED", subOrderId, request.Amount, await FundBalanceAsync(cancellationToken));
        var dayStart = StartOfDay();
        var paidToday = await _context.RefundFundPayouts.AsNoTracking().Where(item => item.CreatedAt >= dayStart).SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        if (paidToday + request.Amount > options.DailyCap)
            return Stopped("DAILY_CAP_EXCEEDED", subOrderId, request.Amount, await FundBalanceAsync(cancellationToken));

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // Wallets are locked in one fixed order (by user id) so two refunds can never wait for each other.
        var ids = new[] { RefundFundAccount.UserId, buyerId, settlement.SellerId }.Distinct().OrderBy(id => id).ToList();
        var locked = new Dictionary<Guid, WalletAccount?>();
        foreach (var id in ids)
            locked[id] = await _context.LockWalletByUserIdAsync(id, cancellationToken);
        var fund = locked[RefundFundAccount.UserId];
        var buyer = locked[buyerId] ?? throw new WalletNotFoundException(buyerId.ToString());
        var seller = locked[settlement.SellerId];
        if (fund is null || fund.AvailableBalance < request.Amount)
            return Stopped("FUND_INSUFFICIENT", subOrderId, request.Amount, fund?.AvailableBalance ?? 0m);
        buyer.EnsureActive();
        fund.EnsureActive();

        // The buyer must have paid by wallet, and nothing may already have been refunded for this order or sub-order.
        var paidByWallet = await _context.WalletTransactions.AsNoTracking()
            .Where(tx => tx.WalletId == buyer.Id && tx.Type == WalletTransactionType.PurchaseDebit && tx.ReferenceId == request.ParentOrderId && tx.Status == TransactionStatus.Success)
            .SumAsync(tx => (decimal?)tx.Amount, cancellationToken) ?? 0m;
        if (paidByWallet < request.Amount)
            throw new BusinessRuleValidationException("NOT_PAID_BY_WALLET", "The buyer did not pay this sub-order by wallet.", 409);
        var alreadyRefunded = await _context.WalletTransactions.AsNoTracking().AnyAsync(tx =>
            tx.WalletId == buyer.Id && RefundTypes.Contains(tx.Type) && (tx.ReferenceId == subOrderId || tx.ReferenceId == request.ParentOrderId), cancellationToken);
        if (alreadyRefunded)
            return Stopped("ALREADY_REFUNDED_OR_UNATTRIBUTABLE", subOrderId, request.Amount, fund.AvailableBalance);

        var now = DateTimeOffset.UtcNow;
        var fundBefore = fund.Balance;
        fund.Deduct(request.Amount);
        var fundTx = NewTx(fund, WalletTransactionType.RefundFundPayout, request.Amount, fundBefore, subOrderId, $"Refund of sub-order {subOrderId} paid from the refund fund");
        var buyerBefore = buyer.Balance;
        buyer.Deposit(request.Amount);
        var buyerTx = NewTx(buyer, WalletTransactionType.RefundFundRefund, request.Amount, buyerBefore, subOrderId, $"Refund of sub-order {subOrderId} from the refund fund");
        _context.WalletTransactions.Add(fundTx);
        _context.WalletTransactions.Add(buyerTx);

        // Take back from the seller only what they can spend now: never escrow, never funds locked for something else.
        var target = Math.Min(request.Amount, settlement.SellerReceiveAmount);
        var recovered = seller is null || seller.Status != "ACTIVE" ? 0m : Math.Min(target, Math.Max(0m, seller.AvailableBalance));
        WalletTransaction? recoveryTx = null;
        if (recovered > 0m && seller is not null)
        {
            var sellerBefore = seller.Balance;
            seller.Deduct(recovered);
            _context.WalletTransactions.Add(NewTx(seller, WalletTransactionType.SellerRecoveryDebit, recovered, sellerBefore, subOrderId, $"Taken back after the refund of sub-order {subOrderId}"));
            var fundBeforeRecovery = fund.Balance;
            fund.Deposit(recovered);
            recoveryTx = NewTx(fund, WalletTransactionType.RefundFundRecovery, recovered, fundBeforeRecovery, subOrderId, $"Taken back from the seller after the refund of sub-order {subOrderId}");
            _context.WalletTransactions.Add(recoveryTx);
        }

        var payout = new RefundFundPayout
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrderId,
            ParentOrderId = request.ParentOrderId,
            BuyerId = buyerId,
            SellerId = settlement.SellerId,
            Amount = request.Amount,
            SellerRecoveryTarget = target,
            SellerRecoveredAmount = recovered,
            SellerShortfall = target - recovered,
            FirstApproverId = first,
            SecondApproverId = second,
            BuyerTransactionId = buyerTx.Id,
            FundTransactionId = fundTx.Id,
            RecoveryTransactionId = recoveryTx?.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.RefundFundPayouts.Add(payout);
        _context.AddAuditLog(
            action: AuditAction.OrderRefund,
            entityName: nameof(RefundFundPayout),
            entityId: payout.Id.ToString(),
            newValues: new { subOrderId, request.ParentOrderId, request.Amount, recovered, payout.SellerShortfall, first, second },
            userId: first.ToString());

        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return Paid(payout, fund.Balance, replayed: false);
    }

    private static WalletTransaction NewTx(WalletAccount wallet, WalletTransactionType type, decimal amount, decimal balanceBefore, Guid reference, string note) => new()
    {
        Id = Guid.NewGuid(),
        WalletId = wallet.Id,
        UserId = wallet.UserId.ToString(),
        Type = type,
        Amount = amount,
        Currency = wallet.Currency.ToString(),
        BalanceBefore = balanceBefore,
        BalanceAfter = wallet.Balance,
        ReferenceId = reference,
        Status = TransactionStatus.Success,
        Note = note,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static RefundFundCaseResult Paid(RefundFundPayout payout, decimal fundBalance, bool replayed) => new(
        "Refunded", null, payout.SubOrderId.ToString(), payout.Amount, payout.SellerRecoveredAmount, payout.SellerShortfall, replayed,
        payout.BuyerTransactionId.ToString(), fundBalance);

    private static RefundFundCaseResult Stopped(string reason, Guid subOrderId, decimal amount, decimal fundBalance) => new(
        NeedsFinance, reason, subOrderId.ToString(), amount, 0m, 0m, false, null, fundBalance);

    private async Task<decimal> FundBalanceAsync(CancellationToken cancellationToken) =>
        await _context.Wallets.AsNoTracking().Where(wallet => wallet.UserId == RefundFundAccount.UserId).Select(wallet => (decimal?)wallet.Balance).FirstOrDefaultAsync(cancellationToken) ?? 0m;

    /// <summary>Midnight of today in Vietnam (UTC+7), as an instant.</summary>
    private static DateTimeOffset StartOfDay()
    {
        var offset = TimeSpan.FromHours(7);
        var local = DateTimeOffset.UtcNow.ToOffset(offset);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, offset);
    }
}
