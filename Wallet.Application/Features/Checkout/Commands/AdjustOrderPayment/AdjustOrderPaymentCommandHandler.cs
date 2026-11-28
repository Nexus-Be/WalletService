namespace Wallet.Application.Features.Checkout.Commands.AdjustOrderPayment;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Features.Checkout;
using global::Wallet.Application.Features.Checkout.Commands.PayOrderWithWallet;
using global::Wallet.Application.Features.Withdrawals;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class AdjustOrderPaymentCommandHandler(IWalletDbContext context)
    : IRequestHandler<AdjustOrderPaymentCommand, WalletOrderTransactionResponse>
{
    public async Task<WalletOrderTransactionResponse> Handle(
        AdjustOrderPaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.BuyerId, out var buyerId) || buyerId == Guid.Empty)
        {
            throw new BusinessRuleValidationException("VAL_INVALID_BUYER_ID", "Buyer ID must be a valid UUID.");
        }

        if (request.DeltaAmount == 0m)
        {
            throw new BusinessRuleValidationException("VAL_INVALID_AMOUNT", "Delta amount must be non-zero.");
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        if (!WithdrawalWorkflow.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_IDEMPOTENCY_KEY",
                "Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.");
        }

        var reason = request.Reason.Trim();
        var fingerprint = ComputeFingerprint(request.OrderId, buyerId, request.DeltaAmount, reason);

        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockOrderPaymentAsync(request.OrderId, cancellationToken);

        var existing = await context.WalletTransactions
            .FirstOrDefaultAsync(
                item => item.ReferenceId == request.OrderId && item.ClientIdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new BusinessRuleValidationException(
                    "IDEMPOTENCY_KEY_REUSED",
                    "The idempotency key was already used for a different payment adjustment.",
                    409);
            }

            await dbTransaction.CommitAsync(cancellationToken);
            return Map(existing);
        }

        var escrowState = await OrderEscrowAccounting.LoadOrReconcileAsync(
            context,
            request.OrderId,
            cancellationToken);

        if (!escrowState.HasPayment)
        {
            throw new BusinessRuleValidationException(
                "PAYMENT_NOT_FOUND",
                "No completed wallet payment was found for the order.",
                404);
        }

        var escrow = escrowState.Escrow;
        if (escrow is null)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_UNAVAILABLE",
                "The order's wallet-payment escrow cannot be safely adjusted.",
                409);
        }

        if (escrow.Status == OrderEscrowStatus.Cancelled)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_CANCELLED",
                "The order escrow was already cancelled and cannot be adjusted.",
                409);
        }

        if (escrow.Status != OrderEscrowStatus.Active)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_ALREADY_RESOLVED",
                "The order escrow was already settled or refunded and cannot be adjusted.",
                409);
        }

        var wallet = await context.LockWalletByUserIdAsync(buyerId, cancellationToken)
            ?? throw new WalletNotFoundException(buyerId.ToString());

        if (escrow.WalletId != wallet.Id)
        {
            throw new BusinessRuleValidationException(
                "PAYMENT_ORDER_CONFLICT",
                "The adjustment buyer does not match the order payer.",
                409);
        }

        var now = DateTimeOffset.UtcNow;
        var balanceBefore = wallet.Balance;
        var escrowOriginalBefore = escrow.OriginalAmount;
        var escrowRemainingBefore = escrow.RemainingAmount;
        WalletTransactionType txType;
        decimal txAmount;

        if (request.DeltaAmount > 0m)
        {
            wallet.EnsureActive();

            if (wallet.AvailableBalance < request.DeltaAmount)
            {
                throw new InsufficientBalanceException(wallet.AvailableBalance, request.DeltaAmount);
            }

            wallet.Deduct(request.DeltaAmount);
            escrow.OriginalAmount += request.DeltaAmount;
            escrow.RemainingAmount += request.DeltaAmount;
            escrow.UpdatedAt = now;
            txType = WalletTransactionType.OrderPaymentAdjustmentDebit;
            txAmount = request.DeltaAmount;
        }
        else
        {
            var refundAmount = Math.Abs(request.DeltaAmount);
            if (escrow.RemainingAmount < refundAmount || escrow.OriginalAmount - refundAmount <= 0m)
            {
                throw new BusinessRuleValidationException(
                    "ORDER_ESCROW_INSUFFICIENT",
                    "The order has insufficient remaining wallet-payment escrow for this adjustment.",
                    409);
            }

            wallet.Deposit(refundAmount);
            escrow.OriginalAmount -= refundAmount;
            escrow.RemainingAmount -= refundAmount;
            if (escrow.RemainingAmount == 0m)
            {
                var hasSettlement = await context.WalletTransactions
                    .AsNoTracking()
                    .AnyAsync(
                        t => t.ReferenceId == request.OrderId &&
                             t.Type == WalletTransactionType.EscrowRelease &&
                             t.Status == TransactionStatus.Success,
                        cancellationToken)
                    || await context.AuditLogs
                        .AsNoTracking()
                        .AnyAsync(
                            a => a.Action == AuditAction.OrderSettlement &&
                                 a.EntityId == request.OrderId.ToString(),
                            cancellationToken);

                escrow.Status = hasSettlement ? OrderEscrowStatus.Settled : OrderEscrowStatus.Refunded;
            }
            escrow.UpdatedAt = now;
            txType = WalletTransactionType.OrderPaymentAdjustmentRefund;
            txAmount = refundAmount;
        }

        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = txType,
            Amount = txAmount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = request.OrderId,
            Note = $"Order payment adjustment: {reason}",
            ClientIdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.WalletTransactions.Add(transaction);
        context.AddAuditLog(
            AuditAction.OrderPaymentAdjustment,
            nameof(OrderEscrow),
            request.OrderId.ToString(),
            oldValues: new
            {
                Balance = balanceBefore,
                EscrowOriginal = escrowOriginalBefore,
                EscrowRemaining = escrowRemainingBefore
            },
            newValues: new
            {
                Balance = wallet.Balance,
                EscrowOriginal = escrow.OriginalAmount,
                EscrowRemaining = escrow.RemainingAmount,
                request.DeltaAmount,
                Reason = reason,
                TransactionId = transaction.Id
            },
            userId: wallet.UserId.ToString());

        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return Map(transaction);
    }

    private static string ComputeFingerprint(Guid orderId, Guid buyerId, decimal deltaAmount, string reason)
    {
        var canonical = string.Join(
            '\n',
            orderId.ToString("D"),
            buyerId.ToString("D"),
            deltaAmount.ToString("0.00", CultureInfo.InvariantCulture),
            reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static WalletOrderTransactionResponse Map(WalletTransaction t) => new(
        t.Id.ToString(),
        t.WalletId.ToString(),
        t.UserId,
        t.Type.ToString(),
        t.Amount,
        t.Currency,
        t.BalanceAfter,
        t.Status.ToString(),
        t.Note,
        t.CreatedAt);
}
