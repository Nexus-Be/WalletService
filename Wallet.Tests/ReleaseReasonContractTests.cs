namespace Wallet.Tests;

using System.Linq;
using Wallet.Application.Features.Holds.Commands.ReleaseHold;
using Xunit;

/// <summary>
/// T23: Auction maps all its release reasons to these five (see WalletReleaseReasonContractTests in Auction.Tests). If Wallet stops accepting
/// one of them, deposits can no longer be released; change both sides together.
/// </summary>
public sealed class ReleaseReasonContractTests
{
    [Theory]
    [InlineData("AUCTION_ENDED_NOT_WINNER")]
    [InlineData("AUCTION_CANCELLED")]
    [InlineData("AUCTION_FAILED")]
    [InlineData("ADMIN_FORCE_CANCEL")]
    [InlineData("BUYER_WITHDRAWN")]
    public void TheFiveReasonsAuctionSendsAreAccepted(string reason)
    {
        var command = new ReleaseHoldCommand(reason) { HoldId = System.Guid.NewGuid(), IdempotencyKey = "release-contract-key-1" };

        Assert.Empty(command.ValidateRequest().ToList());
    }

    [Theory]
    [InlineData("RESERVE_NOT_MET")]
    [InlineData("SECOND_CHANCE_EXPIRED")]
    [InlineData("")]
    public void AnythingElseIsRefused_SoAuctionMustMapItFirst(string reason)
    {
        var command = new ReleaseHoldCommand(reason) { HoldId = System.Guid.NewGuid(), IdempotencyKey = "release-contract-key-1" };

        Assert.NotEmpty(command.ValidateRequest().ToList());
    }
}
