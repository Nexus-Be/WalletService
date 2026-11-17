namespace Wallet.Tests;

using Wallet.Application.Features.Holds.Commands.CaptureHold;
using Wallet.Application.Features.Holds.Commands.RefundCapturedHold;
using Xunit;

public sealed class AuctionCaptureCommandTests
{
    [Fact]
    public void CaptureValidation_RejectsInvalidRecipientsAndDuplicateDestinations()
    {
        var seller = Guid.NewGuid();
        var valid = new CaptureHoldCommand("FORFEIT", null,
            [new("SELLER_WALLET", seller, 3), new("PLATFORM", null, 2)])
            { HoldId = Guid.NewGuid(), IdempotencyKey = "capture:1" };
        Assert.Empty(valid.ValidateRequest());
        Assert.NotEmpty((valid with { Transfers = [new("PLATFORM", null, 1), new("PLATFORM", null, 1)] }).ValidateRequest());
        Assert.NotEmpty((valid with { Transfers = [new("SELLER_WALLET", null, 3)] }).ValidateRequest());
        Assert.NotEmpty((valid with { Transfers = [new("PLATFORM", null, 1.5m)] }).ValidateRequest());
        Assert.NotEmpty((valid with { Purpose = "ORDER_PAYMENT" }).ValidateRequest());
    }

    [Fact]
    public void RefundValidation_RequiresOrderReasonAndKey()
    {
        var valid = new RefundCapturedHoldCommand("SELLER_REJECTED", Guid.NewGuid())
            { HoldId = Guid.NewGuid(), IdempotencyKey = "refund:1" };
        Assert.Empty(valid.ValidateRequest());
        Assert.NotEmpty((valid with { OrderId = Guid.Empty }).ValidateRequest());
        Assert.NotEmpty((valid with { Reason = "" }).ValidateRequest());
        Assert.NotEmpty((valid with { IdempotencyKey = "" }).ValidateRequest());
    }
}
