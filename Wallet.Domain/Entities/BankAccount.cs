using Wallet.Domain.Common;

namespace Wallet.Domain.Entities;

public class BankAccount : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = true;
}