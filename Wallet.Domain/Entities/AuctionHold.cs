namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;
using Wallet.Domain.Enums;

public class AuctionHold : BaseEntity<Guid>
{
    public Guid WalletId { get; init; }
    public Guid OwnerUserId { get; init; }
    public required string ReferenceType { get; init; }
    public required string ReferenceId { get; init; }
    public decimal Amount { get; init; }
    public required string MetadataJson { get; init; }
    public AuctionHoldStatus Status { get; set; } = AuctionHoldStatus.Held;
    public DateTimeOffset? ResolvedAt { get; set; }
}
