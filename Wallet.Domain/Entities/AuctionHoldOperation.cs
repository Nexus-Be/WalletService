namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

public class AuctionHoldOperation : BaseEntity<Guid>
{
    public Guid HoldId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Operation { get; init; }
    public required string RequestFingerprint { get; init; }
    public required string ResponseJson { get; init; }
    public int HttpStatus { get; init; }
}
