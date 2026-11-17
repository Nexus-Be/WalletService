namespace Wallet.Tests;

using System.Text.Json;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Xunit;

public sealed class WalletHeldBalanceTests
{
    [Fact]
    public void WalletResponse_SerializesHeldBalanceAsAliasOfAllLockedFunds()
    {
        var response = new WalletResponse("wallet", "user", 100, 65, 35, "Vnd", "Active", DateTimeOffset.UtcNow);
        var json = JsonSerializer.SerializeToElement(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(100m, json.GetProperty("balance").GetDecimal());
        Assert.Equal(65m, json.GetProperty("availableBalance").GetDecimal());
        Assert.Equal(35m, json.GetProperty("lockedBalance").GetDecimal());
        Assert.Equal(35m, json.GetProperty("heldBalance").GetDecimal());
    }
}
