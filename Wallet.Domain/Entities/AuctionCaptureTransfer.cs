namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

public class AuctionCaptureTransfer : BaseEntity<Guid>
{
    public Guid CaptureId { get; init; }
    public required string RecipientType { get; init; }
    public Guid? RecipientUserId { get; init; }
    public decimal Amount { get; init; }
    public Guid? WalletTransactionId { get; init; }
}
