namespace Wallet.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wallet.Infrastructure;
using Wallet.Infrastructure.Persistence;
using Xunit;

public class InfrastructureConnectionStringTests
{
    [Fact]
    public void AddInfrastructureServices_PreservesEncodedPasswordInPostgresUri()
    {
        const string password = "p@ss;word";
        var connectionUri = $"postgresql://wallet-user:{Uri.EscapeDataString(password)}" +
                            "@localhost:5432/wallet?sslmode=require&channel_binding=require";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionUri
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<WalletDbContext>();
        var connectionString = context.Database.GetDbConnection().ConnectionString;
        var connection = new NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("localhost", connection.Host);
        Assert.Equal("wallet", connection.Database);
        Assert.Equal("wallet-user", connection.Username);
        Assert.Equal(password, connection.Password);
        Assert.Equal("Require", connection["Channel Binding"].ToString());
    }
}