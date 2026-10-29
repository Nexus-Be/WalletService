namespace Wallet.Application.Consumers.Commerce;

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;

public sealed record WalletDebitResult(
    bool Succeeded,
    string TransactionId,
    decimal RemainingBalance,
    string? FailureReason);

public sealed class WalletDebitProcessor
{
    private readonly IWalletDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;

    public WalletDebitProcessor(IWalletDbContext context, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<WalletDebitResult> ProcessAsync(
        Guid orderId,
        Guid userId,
        decimal amount,
        bool legacyEventContract,
        CancellationToken cancellationToken)
    {
        await using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (amount <= 0)
        {
            const string reason = "Payment amount is invalid.";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, 0m, reason);
        }

        var wallet = await _context.LockWalletByUserIdAsync(userId, cancellationToken);
        if (wallet is null)
        {
            const string reason = "User does not have a wallet account.";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, 0m, reason);
        }

        var existing = await _context.WalletTransactions
            .AsNoTracking()
            .Where(t => t.ReferenceId == orderId && t.Type == WalletTransactionType.PurchaseDebit)
            .Select(t => new { t.Id, t.WalletId, t.Status, t.Amount })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == TransactionStatus.Success &&
                existing.WalletId == wallet.Id &&
                existing.Amount == amount)
            {
                await PublishSuccessAsync(
                    orderId,
                    existing.Id.ToString(),
                    amount,
                    legacyEventContract,
                    cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await dbTransaction.CommitAsync(cancellationToken);
                return new WalletDebitResult(true, existing.Id.ToString(), wallet.Balance, null);
            }

            const string reason = "A payment transaction already exists with different data.";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, wallet.Balance, reason);
        }

        if (!string.Equals(wallet.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            const string reason = "Wallet is not active.";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, wallet.Balance, reason);
        }

        if (wallet.AvailableBalance < amount)
        {
            const string reason = "Insufficient wallet balance to pay for the order.";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, wallet.AvailableBalance, reason);
        }

        var balanceBefore = wallet.Balance;
        wallet.Deduct(amount);
        var walletTransaction = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.PurchaseDebit,
            Amount = amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            ReferenceId = orderId,
            Status = TransactionStatus.Success,
            Note = $"Payment for order #{orderId}"
        };
        _context.WalletTransactions.Add(walletTransaction);
        await PublishSuccessAsync(
            orderId,
            walletTransaction.Id.ToString(),
            amount,
            legacyEventContract,
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return new WalletDebitResult(true, walletTransaction.Id.ToString(), wallet.Balance, null);
    }

    private Task PublishSuccessAsync(
        Guid orderId,
        string transactionId,
        decimal amount,
        bool legacyEventContract,
        CancellationToken cancellationToken)
    {
        var formattedAmount = amount.ToString("0.00", CultureInfo.InvariantCulture);
        var timestamp = DateTimeOffset.UtcNow;
        return legacyEventContract
            ? _publishEndpoint.Publish<IPaymentCompletedEvent>(new
            {
                OrderId = orderId.ToString(),
                TransactionId = transactionId,
                Amount = formattedAmount,
                Timestamp = timestamp
            }, cancellationToken)
            : _publishEndpoint.Publish<IWalletDebitSucceededEvent>(new
            {
                OrderId = orderId.ToString(),
                TransactionId = transactionId,
                Amount = formattedAmount,
                Timestamp = timestamp
            }, cancellationToken);
    }

    private Task PublishFailureAsync(
        Guid orderId,
        string reason,
        bool legacyEventContract,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        return legacyEventContract
            ? _publishEndpoint.Publish<IPaymentFailedEvent>(new
            {
                OrderId = orderId.ToString(),
                Reason = reason,
                Timestamp = timestamp
            }, cancellationToken)
            : _publishEndpoint.Publish<IWalletDebitFailedEvent>(new
            {
                OrderId = orderId.ToString(),
                Reason = reason,
                Timestamp = timestamp
            }, cancellationToken);
    }
}
