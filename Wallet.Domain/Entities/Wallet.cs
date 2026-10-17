namespace Wallet.Domain.Entities;

using global::Wallet.Domain.Common;
using global::Wallet.Domain.Exceptions;

public class WalletAccount : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public decimal Balance { get; set; } = 0m;
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "ACTIVE";

    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();

    public void Deposit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Số tiền nạp phải lớn hơn 0.", nameof(amount));

        Balance += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Withdraw(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Số tiền rút phải lớn hơn 0.", nameof(amount));

        if (Balance < amount)
            throw new InsufficientBalanceException(Balance, amount);

        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deduct(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Số tiền trừ phải lớn hơn 0.", nameof(amount));

        if (Balance < amount)
            throw new InsufficientBalanceException(Balance, amount);

        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
