namespace Wallet.Domain.Entities;

using global::Wallet.Domain.Common;

public class WalletTransaction : BaseEntity
{
    public Guid WalletId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal BalanceAfter { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string Note { get; set; } = string.Empty;

    public WalletAccount? Wallet { get; set; }
}
