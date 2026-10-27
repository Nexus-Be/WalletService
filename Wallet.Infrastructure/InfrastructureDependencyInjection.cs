namespace Wallet.Infrastructure;

using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.Consumers;
using Wallet.Infrastructure.Payments;
using Wallet.Infrastructure.Persistence;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found in configuration.");

        services.AddDbContext<WalletDbContext>(options =>
        {
            options.UseNpgsql(connectionString, b =>
            {
                b.MigrationsAssembly(typeof(WalletDbContext).Assembly.FullName);
                b.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            });
        });

        services.AddScoped<IWalletDbContext>(sp => sp.GetRequiredService<WalletDbContext>());
        services.AddScoped<WalletDebitProcessor>();
        services.AddSingleton<IVnPayService, VnPayService>();
        services.AddSingleton<IDirectPaymentGateway, ConfiguredDirectPaymentGateway>();

        // MassTransit 8.4.1 / 8.3.0 + RabbitMQ + Transactional Outbox
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            x.AddConsumer<StockReservedConsumer>();
            x.AddConsumer<UserRegisteredConsumer>();
            x.AddConsumer<OrderCheckoutRequestedConsumer>();
            x.AddConsumer<OrderCancelledConsumer>();

            x.AddEntityFrameworkOutbox<WalletDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitHost = configuration["RabbitMq:Host"] ?? "localhost";
                var rabbitPort = ushort.TryParse(configuration["RabbitMq:Port"], out var port) ? port : (ushort)5672;
                var rabbitUser = configuration["RabbitMq:User"] ?? "guest";
                var rabbitPass = configuration["RabbitMq:Password"] ?? string.Empty;

                cfg.Host(rabbitHost, rabbitPort, "/", h =>
                {
                    h.Username(rabbitUser);
                    h.Password(rabbitPass);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}