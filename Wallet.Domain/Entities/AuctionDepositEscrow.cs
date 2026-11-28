namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

public class AuctionDepositEscrow : BaseEntity<Guid>
{
    public Guid HoldId { get; init; }
    public Guid OrderId { get; init; }
    public decimal OriginalAmount { get; init; }
    public decimal RemainingAmount { get; set; }
}
