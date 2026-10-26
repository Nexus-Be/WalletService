namespace Wallet.Application.Consumers;

using System.Globalization;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wallet.Application.Common.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;
using Wallet.Domain.Exceptions;

public sealed class UserRegisteredConsumer : IConsumer<IUserRegisteredEvent>
{
    private readonly IWalletDbContext _context;

    public UserRegisteredConsumer(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task Consume(ConsumeContext<IUserRegisteredEvent> context)
    {
        if (!Guid.TryParse(context.Message.UserId, out var userId) || userId == Guid.Empty)
            throw new InvalidOperationException("UserRegistered event has an invalid UserId.");

        if (await _context.Wallets.AnyAsync(w => w.UserId == userId, context.CancellationToken))
            return;

        _context.Wallets.Add(new WalletAccount
        {
            UserId = userId,
            Balance = 0m,
            LockedBalance = 0m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        });
        await _context.SaveChangesAsync(context.CancellationToken);
    }
}

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
            const string reason = "Số tiền thanh toán không hợp lệ";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, 0m, reason);
        }

        var wallet = await _context.LockWalletByUserIdAsync(userId, cancellationToken);
        if (wallet is null)
        {
            const string reason = "Người dùng chưa có tài khoản ví";
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

            const string reason = "Giao dịch thanh toán đã tồn tại với dữ liệu khác";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, wallet.Balance, reason);
        }

        if (!string.Equals(wallet.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            const string reason = "Ví không ở trạng thái hoạt động";
            await PublishFailureAsync(orderId, reason, legacyEventContract, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return new WalletDebitResult(false, string.Empty, wallet.Balance, reason);
        }

        if (wallet.AvailableBalance < amount)
        {
            const string reason = "Số dư ví không đủ để thanh toán đơn hàng";
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
            Note = $"Thanh toán đơn hàng #{orderId}"
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

public sealed class OrderCheckoutRequestedConsumer : IConsumer<IOrderCheckoutRequestedEvent>
{
    private readonly WalletDebitProcessor _processor;

    public OrderCheckoutRequestedConsumer(WalletDebitProcessor processor)
    {
        _processor = processor;
    }

    public async Task Consume(ConsumeContext<IOrderCheckoutRequestedEvent> context)
    {
        var message = context.Message;
        if (!Guid.TryParse(message.OrderId, out var orderId) || orderId == Guid.Empty ||
            !Guid.TryParse(message.UserId, out var userId) || userId == Guid.Empty ||
            !decimal.TryParse(message.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            await PublishFailure(context, message.OrderId, "Dữ liệu thanh toán không hợp lệ");
            return;
        }

        await _processor.ProcessAsync(orderId, userId, amount, false, context.CancellationToken);
    }

    private static Task PublishFailure(
        ConsumeContext<IOrderCheckoutRequestedEvent> context,
        string orderId,
        string reason)
    {
        return context.Publish<IWalletDebitFailedEvent>(new
        {
            OrderId = orderId,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}

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
            Note = $"Hoàn tiền đơn hàng #{orderId}: {message.Reason}"
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