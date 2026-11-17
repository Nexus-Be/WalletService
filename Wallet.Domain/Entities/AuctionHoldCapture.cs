namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

public class AuctionHoldCapture : BaseEntity<Guid>
{
    public Guid HoldId { get; init; }
    public required string Purpose { get; init; }
    public Guid? OrderId { get; init; }
    public required string RequestFingerprint { get; init; }
}
