namespace Wallet.Domain.Entities;

using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

public class WalletAccount : Wallet
{
    public string Status { get; set; } = "ACTIVE";
    public decimal AvailableBalance => Balance - LockedBalance;

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

        EnsureAvailableBalance(amount);

        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deduct(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Số tiền trừ phải lớn hơn 0.", nameof(amount));

        EnsureAvailableBalance(amount);

        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void LockFunds(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Số tiền khóa phải lớn hơn 0.", nameof(amount));

        EnsureAvailableBalance(amount);
        LockedBalance += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReleaseLockedFunds(decimal amount)
    {
        if (amount <= 0 || amount > LockedBalance)
            throw new ArgumentOutOfRangeException(nameof(amount), "Số tiền giải phóng vượt quá số dư bị khóa.");

        LockedBalance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void CompleteLockedWithdrawal(decimal amount)
    {
        if (amount <= 0 || amount > LockedBalance || amount > Balance)
            throw new ArgumentOutOfRangeException(nameof(amount), "Số tiền rút vượt quá số dư bị khóa.");

        LockedBalance -= amount;
        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureAvailableBalance(decimal amount)
    {
        if (AvailableBalance < amount)
            throw new InsufficientBalanceException(AvailableBalance, amount);
    }
}