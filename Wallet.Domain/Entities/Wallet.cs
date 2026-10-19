using Wallet.Domain.Base;
using Wallet.Domain.Enums;

namespace Wallet.Domain.Entities;

public class Wallet : AuditableEntity<Guid>
{
    public required Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public decimal LockedBalance { get; set; }
    public required CurrencyCode Currency { get; set; } = CurrencyCode.Vnd;

    // Navigation
    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
    public ICollection<WalletTopup> Topups { get; set; } = new List<WalletTopup>();
    public ICollection<WithdrawalRequest> WithdrawalRequests { get; set; } = new List<WithdrawalRequest>();
}
