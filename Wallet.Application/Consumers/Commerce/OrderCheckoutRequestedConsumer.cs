namespace Wallet.Application.Consumers.Commerce;

using System;
using System.Globalization;
using System.Threading.Tasks;
using MassTransit;
using Wallet.Domain.Events;

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
            await PublishFailure(context, message.OrderId, "Payment data is invalid.");
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
