namespace Wallet.Application;

using Microsoft.Extensions.DependencyInjection;
using Wallet.Application.Services.Admin;
using Wallet.Application.Services.Wallet;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<IAdminWalletService, AdminWalletService>();

        return services;
    }
}
