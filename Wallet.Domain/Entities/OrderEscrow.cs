namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;

public class OrderEscrow : BaseEntity<Guid>
{
    public Guid OrderId { get; init; }
    public Guid WalletId { get; init; }
    public decimal OriginalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
}
