namespace Wallet.Application.Consumers.Commerce;

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wallet.Application.Common.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;
using Wallet.Domain.Exceptions;

public sealed class OrderCancelledConsumer : IConsumer<IOrderCancelledEvent>
{
    private readonly IWalletDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderCancelledConsumer> _logger;

    public OrderCancelledConsumer(
        IWalletDbContext context,
        IPublishEndpoint publishEndpoint,
        ILogger<OrderCancelledConsumer> logger)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IOrderCancelledEvent> context)
    {
        var message = context.Message;
        if (!Guid.TryParse(message.OrderId, out var orderId) || orderId == Guid.Empty ||
            !Guid.TryParse(message.CancellationId, out var cancellationId) || cancellationId == Guid.Empty ||
            !Guid.TryParse(message.UserId, out var userId) || userId == Guid.Empty ||
            !decimal.TryParse(message.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            _logger.LogWarning("OrderCancelled event contains invalid identifiers or amount for order {OrderId}",
                message.OrderId);
            throw new InvalidOperationException("OrderCancelled event is invalid.");
        }

        await using var dbTransaction = await _context.Database.BeginTransactionAsync(context.CancellationToken);
        var wallet = await _context.LockWalletByUserIdAsync(userId, context.CancellationToken);
        if (wallet is null)
            throw new WalletNotFoundException(message.UserId);

        var originalPayment = await _context.WalletTransactions
            .SingleOrDefaultAsync(
                t => t.ReferenceId == orderId &&
                     t.Type == WalletTransactionType.PurchaseDebit &&
                     t.Status == TransactionStatus.Success,
                context.CancellationToken);
        if (originalPayment is null)
            throw new BusinessRuleValidationException("ORDER_NOT_PAID");
        if (originalPayment.WalletId != wallet.Id)
            throw new BusinessRuleValidationException("ORDER_NOT_PAID");

        var existingRefund = await _context.WalletTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.ReferenceId == cancellationId && t.Type == WalletTransactionType.RefundCredit,
                context.CancellationToken);
        if (existingRefund is not null)
        {
            if (existingRefund.WalletId != wallet.Id || existingRefund.Amount != amount)
                throw new BusinessRuleValidationException("Refund idempotency key was reused with different data.");

            await _publishEndpoint.Publish<IWalletRefundedEvent>(new
            {
                OrderId = orderId.ToString(),
                TransactionId = existingRefund.Id.ToString(),
                Amount = existingRefund.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                Timestamp = DateTimeOffset.UtcNow
            }, context.CancellationToken);
            await _context.SaveChangesAsync(context.CancellationToken);
            await dbTransaction.CommitAsync(context.CancellationToken);
            return;
        }

        var alreadyRefunded = await _context.WalletTransactions
            .Where(t => t.Type == WalletTransactionType.RefundCredit &&
                        t.Note != null &&
                        t.Note.Contains($"#{orderId}"))
            .SumAsync(t => (decimal?)t.Amount, context.CancellationToken) ?? 0m;
        if (amount > originalPayment.Amount - alreadyRefunded)
            throw new BusinessRuleValidationException("REFUND_AMOUNT_EXCEEDS_ORDER_TOTAL");

        var balanceBefore = wallet.Balance;
        wallet.Deposit(amount);
        var refund = new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.RefundCredit,
            Amount = amount,
            Currency = wallet.Currency.ToString(),
            BalanceBefore = balanceBefore,
            BalanceAfter = wallet.Balance,
            ReferenceId = cancellationId,
            Status = TransactionStatus.Success,
            Note = $"Refund for order #{orderId}: {message.Reason}"
        };
        _context.WalletTransactions.Add(refund);
        await _publishEndpoint.Publish<IWalletRefundedEvent>(new
        {
            OrderId = orderId.ToString(),
            TransactionId = refund.Id.ToString(),
            Amount = amount.ToString("0.00", CultureInfo.InvariantCulture),
            Timestamp = DateTimeOffset.UtcNow
        }, context.CancellationToken);
        await _context.SaveChangesAsync(context.CancellationToken);
        await dbTransaction.CommitAsync(context.CancellationToken);
    }
}
