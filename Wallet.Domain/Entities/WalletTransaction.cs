using Wallet.Domain.Base;
using Wallet.Domain.Enums;

namespace Wallet.Domain.Entities;

public class WalletTransaction : BaseEntity<Guid>
{
    public required Guid WalletId { get; set; }
    public required WalletTransactionType Type { get; set; }
    public required decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public Guid? ReferenceId { get; set; }
    public required TransactionStatus Status { get; set; } = TransactionStatus.Pending;

    // Navigation
    public Wallet? Wallet { get; set; }
}
