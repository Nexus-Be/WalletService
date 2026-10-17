namespace Wallet.Application.Consumers;

using System.Globalization;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wallet.Application.Common.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Events;
using Wallet.Domain.Exceptions;

public class StockReservedConsumer : IConsumer<IStockReservedEvent>
{
    private readonly IWalletDbContext _context;
    private readonly ILogger<StockReservedConsumer> _logger;

    public StockReservedConsumer(IWalletDbContext context, ILogger<StockReservedConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IStockReservedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Nhận StockReservedEvent cho đơn hàng {OrderId}, UserId {UserId}, Số tiền {Amount}",
            message.OrderId, message.UserId, message.TotalAmount);

        if (!decimal.TryParse(message.TotalAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var requiredAmount) || requiredAmount <= 0)
        {
            _logger.LogWarning("Số tiền đơn hàng không hợp lệ: {TotalAmount}", message.TotalAmount);
            await context.Publish<IPaymentFailedEvent>(new
            {
                OrderId = message.OrderId,
                Reason = "Số tiền thanh toán không hợp lệ",
                Timestamp = DateTimeOffset.UtcNow
            });
            return;
        }

        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.UserId == message.UserId, context.CancellationToken);

        if (wallet == null)
        {
            _logger.LogWarning("Không tìm thấy ví cho User {UserId}", message.UserId);
            await context.Publish<IPaymentFailedEvent>(new
            {
                OrderId = message.OrderId,
                Reason = "Người dùng chưa có tài khoản ví",
                Timestamp = DateTimeOffset.UtcNow
            });
            return;
        }

        try
        {
            wallet.Deduct(requiredAmount);

            var tx = new WalletTransaction
            {
                WalletId = wallet.Id,
                UserId = wallet.UserId,
                Type = "ORDER_PAYMENT",
                Amount = requiredAmount,
                Currency = wallet.Currency,
                BalanceAfter = wallet.Balance,
                Status = "SUCCESS",
                Note = $"Thanh toán đơn hàng #{message.OrderId}"
            };

            _context.WalletTransactions.Add(tx);
            await _context.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Thanh toán thành công đơn hàng {OrderId}. Số dư còn lại: {Balance}",
                message.OrderId, wallet.Balance);

            await context.Publish<IPaymentCompletedEvent>(new
            {
                OrderId = message.OrderId,
                TransactionId = tx.Id.ToString(),
                Amount = requiredAmount.ToString("0.00", CultureInfo.InvariantCulture),
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (InsufficientBalanceException ex)
        {
            _logger.LogWarning("Số dư ví không đủ để thanh toán đơn {OrderId}: {Message}", message.OrderId, ex.Message);

            await context.Publish<IPaymentFailedEvent>(new
            {
                OrderId = message.OrderId,
                Reason = "Số dư ví không đủ để thanh toán đơn hàng",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }
}
