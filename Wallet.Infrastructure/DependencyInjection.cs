namespace Wallet.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wallet.Application.Interfaces;
using Wallet.Infrastructure.Payments;
using Wallet.Infrastructure.Persistence;
using Wallet.Infrastructure.Services.Notification;

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
        services.AddHttpClient<IStripeCheckoutClient, StripeCheckoutClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IStripeWebhookVerifier, StripeWebhookVerifier>();

        var notificationBaseUrl = Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_URL")
            ?? configuration["NOTIFICATION_SERVICE_URL"]
            ?? configuration["Services:Notification:BaseUrl"]
            ?? "http://localhost:8007/";

        services.AddHttpClient<INotificationClient, NotificationClient>(client =>
        {
            client.BaseAddress = new Uri(notificationBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient("reconciliation-source", client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<global::Wallet.Application.Common.Ledger.IReconciliationSourceClient, Services.HttpReconciliationSourceClient>();
        services.AddScoped<global::Wallet.Application.Common.Ledger.JournalPoster>();

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
                var connectionBuilder = new NpgsqlConnectionStringBuilder
                {
                    Host = uri.Host,
                    Port = port,
                    Database = db,
                    Username = user,
                    Password = pass,
                    SslMode = SslMode.Require
                };
                var queryParameters = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var channelBinding = queryParameters["channel_binding"];
                if (!string.IsNullOrWhiteSpace(channelBinding))
                {
                    connectionBuilder["Channel Binding"] = channelBinding;
                }

                return connectionBuilder.ConnectionString;
            }
            catch
            {
                return trimmed;
            }
        }

        return trimmed;
    }
}