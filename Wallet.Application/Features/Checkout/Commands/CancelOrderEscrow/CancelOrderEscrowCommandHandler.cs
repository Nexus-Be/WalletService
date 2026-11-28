namespace Wallet.Application.Features.Checkout.Commands.CancelOrderEscrow;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Features.Checkout;
using global::Wallet.Application.Features.Withdrawals;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

internal sealed class CancelOrderEscrowCommandHandler(IWalletDbContext context)
    : IRequestHandler<CancelOrderEscrowCommand, CancelOrderEscrowResponse>
{
    public async Task<CancelOrderEscrowResponse> Handle(
        CancelOrderEscrowCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OrderId == Guid.Empty)
        {
            throw new BusinessRuleValidationException("VAL_INVALID_ORDER_ID", "Order ID must be a valid UUID.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 3 || reason.Length > 500)
        {
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Reason must be between 3 and 500 characters.");
        }

        var idempotencyKey = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (!WithdrawalWorkflow.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_IDEMPOTENCY_KEY",
                "Idempotency-Key header is required and must be 1 to 100 ASCII letters, digits, hyphens, or underscores.");
        }

        Guid? expectedBuyerId = null;
        if (request.ExpectedBuyerId is not null)
        {
            if (!Guid.TryParse(request.ExpectedBuyerId, out var parsedBuyer) || parsedBuyer == Guid.Empty)
            {
                throw new BusinessRuleValidationException(
                    "VAL_INVALID_BUYER_ID",
                    "Expected buyer ID must be a valid UUID.");
            }

            expectedBuyerId = parsedBuyer;
        }

        if (request.ExpectedAmount is decimal expectedAmount &&
            (expectedAmount <= 0m ||
             expectedAmount > 9999999999999999.99m ||
             expectedAmount != decimal.Round(expectedAmount, 2)))
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_AMOUNT",
                "Expected amount must be a positive decimal with at most two fractional digits.");
        }

        var fingerprint = ComputeFingerprint(request.OrderId, expectedBuyerId, request.ExpectedAmount, reason);

        await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockOrderPaymentAsync(request.OrderId, cancellationToken);

        var existing = await context.WalletTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ReferenceId == request.OrderId && item.ClientIdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.Type != WalletTransactionType.EscrowCancellationRefund ||
                !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new BusinessRuleValidationException(
                    "IDEMPOTENCY_KEY_REUSED",
                    "The idempotency key was already used for a different order operation.",
                    409);
            }

            var replayedEscrow = await context.OrderEscrows
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.OrderId == request.OrderId, cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return Map(existing, replayedEscrow, isReplay: true);
        }

        var escrowState = await OrderEscrowAccounting.LoadOrReconcileAsync(
            context,
            request.OrderId,
            cancellationToken);

        if (!escrowState.HasPayment)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_NOT_FOUND",
                "No wallet-payment escrow was found for the order.",
                404);
        }

        var escrow = escrowState.Escrow;
        if (escrow is null)
        {
            if (escrowState.HasResolvedOperation)
            {
                throw new BusinessRuleValidationException(
                    "ORDER_ESCROW_ALREADY_RESOLVED",
                    "The order escrow was already settled, refunded, or cancelled.",
                    409);
            }

            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_UNAVAILABLE",
                "The order's wallet-payment escrow cannot be safely cancelled.",
                409);
        }

        if (escrow.Status == OrderEscrowStatus.Cancelled)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_ALREADY_CANCELLED",
                "The order escrow was already cancelled.",
                409);
        }

        if (escrow.Status is OrderEscrowStatus.Settled or OrderEscrowStatus.Refunded ||
            escrow.RemainingAmount == 0m)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_ALREADY_RESOLVED",
                "The order escrow was already settled or refunded and cannot be cancelled.",
                409);
        }

        if (escrow.RemainingAmount != escrow.OriginalAmount)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_PARTIALLY_RESOLVED",
                "The order escrow has already been partially settled or refunded and cannot be cancelled in full.",
                409);
        }

        if (request.ExpectedAmount is decimal expectedAmt && expectedAmt != escrow.RemainingAmount)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_AMOUNT_MISMATCH",
                "The expected amount does not match the remaining order escrow balance.",
                409);
        }

        var wallet = await context.LockWalletByIdAsync(escrow.WalletId, cancellationToken)
            ?? throw new InvalidOperationException("The order escrow wallet was not found.");

        if (expectedBuyerId is Guid expectedBuyer && wallet.UserId != expectedBuyer)
        {
            throw new BusinessRuleValidationException(
                "ORDER_ESCROW_OWNERSHIP_CONFLICT",
                "The expected buyer does not match the wallet that funded the order escrow.",
                409);
        }

        var now = DateTimeOffset.UtcNow;
        var amountToReturn = escrow.RemainingAmount;
        var balanceBefore = wallet.Balance;
        var escrowStatusBefore = escrow.Status;
        var escrowRemainingBefore = escrow.RemainingAmount;

        // Intentionally allow returning funds to a frozen wallet so money is not stranded in escrow.
        wallet.Deposit(amountToReturn);
        escrow.RemainingAmount = 0m;
        escrow.Status = OrderEscrowStatus.Cancelled;
        escrow.CancelledAt = now;
        escrow.CancelReason = reason;
        escrow.UpdatedAt = now;

        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.EscrowCancellationRefund,
            Amount = amountToReturn,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            ReferenceId = request.OrderId,
            Note = $"Order escrow cancelled: {reason}",
            ClientIdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.WalletTransactions.Add(transaction);
        context.AddAuditLog(
            AuditAction.OrderEscrowCancel,
            nameof(OrderEscrow),
            request.OrderId.ToString(),
            oldValues: new
            {
                Balance = balanceBefore,
                EscrowStatus = escrowStatusBefore.ToString(),
                EscrowRemaining = escrowRemainingBefore,
                WalletStatus = wallet.Status
            },
            newValues: new
            {
                Balance = wallet.Balance,
                EscrowStatus = escrow.Status.ToString(),
                EscrowRemaining = escrow.RemainingAmount,
                WalletStatus = wallet.Status,
                ReturnedAmount = amountToReturn,
                Reason = reason,
                IdempotencyKey = idempotencyKey,
                TransactionId = transaction.Id,
                CancelledAt = now
            },
            userId: wallet.UserId.ToString(),
            changedColumns: new[]
            {
                nameof(WalletAccount.Balance),
                nameof(OrderEscrow.RemainingAmount),
                nameof(OrderEscrow.Status),
                nameof(OrderEscrow.CancelledAt),
                nameof(OrderEscrow.CancelReason)
            },
            traceId: Activity.Current?.Id);

        await context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        return Map(transaction, escrow, isReplay: false);
    }

    private static string ComputeFingerprint(
        Guid orderId,
        Guid? expectedBuyerId,
        decimal? expectedAmount,
        string reason)
    {
        var canonical = string.Join(
            '\n',
            "cancel-order-escrow",
            orderId.ToString("D"),
            expectedBuyerId?.ToString("D") ?? string.Empty,
            expectedAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty,
            reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static CancelOrderEscrowResponse Map(WalletTransaction tx, OrderEscrow? escrow, bool isReplay) => new(
        tx.Id.ToString(),
        (tx.ReferenceId ?? escrow?.OrderId ?? Guid.Empty).ToString(),
        tx.WalletId.ToString(),
        tx.UserId ?? string.Empty,
        tx.Type.ToString(),
        tx.Amount,
        tx.Currency ?? CurrencyCode.Vnd.ToString(),
        tx.BalanceBefore,
        tx.BalanceAfter,
        (escrow?.Status ?? OrderEscrowStatus.Cancelled).ToString(),
        tx.Status.ToString(),
        tx.Note ?? string.Empty,
        escrow?.CancelledAt ?? tx.CreatedAt,
        isReplay);
}
