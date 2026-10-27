namespace Wallet.Application.Consumers;

using System.Globalization;
using MassTransit;
using Microsoft.Extensions.Logging;
using Wallet.Domain.Events;

public sealed class StockReservedConsumer : IConsumer<IStockReservedEvent>
{
    private readonly WalletDebitProcessor _processor;
    private readonly ILogger<StockReservedConsumer> _logger;

    public StockReservedConsumer(WalletDebitProcessor processor, ILogger<StockReservedConsumer> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IStockReservedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "Received stock reservation for order {OrderId}, user {UserId}, amount {Amount}",
            message.OrderId,
            message.UserId,
            message.TotalAmount);

        if (!Guid.TryParse(message.OrderId, out var orderId) || orderId == Guid.Empty ||
            !Guid.TryParse(message.UserId, out var userId) || userId == Guid.Empty ||
            !decimal.TryParse(message.TotalAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            await PublishFailure(context, message.OrderId, "Payment data is invalid.");
            return;
        }

        var result = await _processor.ProcessAsync(orderId, userId, amount, true, context.CancellationToken);
        if (!result.Succeeded)
            _logger.LogWarning("Payment failed for order {OrderId}: {Reason}", orderId, result.FailureReason);
    }

    private static Task PublishFailure(
        ConsumeContext<IStockReservedEvent> context,
        string orderId,
        string reason)
    {
        return context.Publish<IPaymentFailedEvent>(new
        {
            OrderId = orderId,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}