namespace Wallet.Application.Consumers;

using MassTransit;
using Wallet.Application.Consumers.Commerce;
using Wallet.Application.Consumers.User;

public static class ConsumerExtensions
{
    public static IBusRegistrationConfigurator AddApplicationConsumers(this IBusRegistrationConfigurator configurator)
    {
        // Commerce Saga Events
        configurator.AddConsumer<StockReservedConsumer>();
        configurator.AddConsumer<OrderCheckoutRequestedConsumer>();
        configurator.AddConsumer<OrderCancelledConsumer>();

        // User Events
        configurator.AddConsumer<UserRegisteredConsumer>();

        return configurator;
    }
}
