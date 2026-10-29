using Wallet.Domain.Exceptions;

namespace Wallet.Domain.Entities;

public class WalletAccount : Wallet
{
    public string Status { get; set; } = "ACTIVE";

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