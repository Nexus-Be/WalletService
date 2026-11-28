namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;
using Wallet.Domain.Enums;

public class OrderEscrow : BaseEntity<Guid>
{
    public Guid OrderId { get; init; }
    public Guid WalletId { get; init; }
    public decimal OriginalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public OrderEscrowStatus Status { get; set; } = OrderEscrowStatus.Active;
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
}
