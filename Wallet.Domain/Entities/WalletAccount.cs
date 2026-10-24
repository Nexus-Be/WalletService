namespace Wallet.Domain.Entities;

using Wallet.Domain.Base;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

public class WalletAccount : AuditableEntity<Guid>
{
    public required Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public decimal LockedBalance { get; set; }
    public required CurrencyCode Currency { get; set; } = CurrencyCode.Vnd;
    public string Status { get; set; } = "ACTIVE";

    // Navigation
    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
    public ICollection<WalletTopup> Topups { get; set; } = new List<WalletTopup>();
    public ICollection<WithdrawalRequest> WithdrawalRequests { get; set; } = new List<WithdrawalRequest>();

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
