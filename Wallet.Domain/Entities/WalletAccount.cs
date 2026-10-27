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
            throw new ArgumentException("Deposit amount must be greater than zero.", nameof(amount));

        Balance += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Withdraw(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Withdrawal amount must be greater than zero.", nameof(amount));

        EnsureAvailableBalance(amount);

        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deduct(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Deduction amount must be greater than zero.", nameof(amount));

        EnsureAvailableBalance(amount);

        Balance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void LockFunds(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount to lock must be greater than zero.", nameof(amount));

        EnsureAvailableBalance(amount);
        LockedBalance += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReleaseLockedFunds(decimal amount)
    {
        if (amount <= 0 || amount > LockedBalance)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount to release exceeds the locked balance.");

        LockedBalance -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void CompleteLockedWithdrawal(decimal amount)
    {
        if (amount <= 0 || amount > LockedBalance || amount > Balance)
            throw new ArgumentOutOfRangeException(nameof(amount), "Withdrawal amount exceeds the locked balance.");

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