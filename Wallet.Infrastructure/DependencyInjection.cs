namespace Wallet.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Application.Interfaces;
using Wallet.Infrastructure.Payments;
using Wallet.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        var rawConnection = configuration.GetConnectionString("DefaultConnection") 
            ?? configuration["CONNECTION_STRING"]
            ?? configuration["WALLET_CONNECTION_STRING"]
            ?? configuration["WALLET_DB_CONNECTION"]
            ?? Environment.GetEnvironmentVariable("WALLET_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(rawConnection) || rawConnection.Contains("REPLACE_WITH"))
        {
            throw new InvalidOperationException("Database connection string for WalletService is not configured. Set 'ConnectionStrings:DefaultConnection' or environment variable 'WALLET_CONNECTION_STRING'.");
        }
        var connectionString = NormalizeConnectionString(rawConnection);

        services.AddDbContext<WalletDbContext>(options =>
        {
            options.UseNpgsql(connectionString, b =>
            {
                b.MigrationsAssembly(typeof(WalletDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IWalletDbContext>(sp => sp.GetRequiredService<WalletDbContext>());
        services.AddSingleton<IVnPayService, VnPayService>();
        services.AddSingleton<IDirectPaymentGateway, ConfiguredDirectPaymentGateway>();

        return services;
    }

    private static string NormalizeConnectionString(string? connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return string.Empty;
        var trimmed = connStr.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(trimmed);
                var userInfo = uri.UserInfo.Split(':');
                var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "postgres";
                var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
                var port = uri.Port > 0 ? uri.Port : 5432;
                var db = uri.AbsolutePath.TrimStart('/');
                return $"Host={uri.Host};Port={port};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
            }
            catch
            {
                return trimmed;
            }
        }
        return trimmed;
    }
}
