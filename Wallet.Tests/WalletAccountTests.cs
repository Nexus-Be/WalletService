using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Xunit;

namespace Wallet.Tests;

public class WalletAccountTests
{
    [Fact]
    public void AvailableBalance_ExcludesLockedFunds()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 25m);

        Assert.Equal(75m, wallet.AvailableBalance);
    }

    [Fact]
    public void Deposit_IncreasesBalanceAndAvailableBalance()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 25m);

        wallet.Deposit(50m);

        Assert.Equal(150m, wallet.Balance);
        Assert.Equal(125m, wallet.AvailableBalance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Deposit_RejectsNonPositiveAmount(decimal amount)
    {
        var wallet = CreateWallet(balance: 100m);

        Assert.Throws<ArgumentException>(() => wallet.Deposit(amount));

        Assert.Equal(100m, wallet.Balance);
    }

    [Fact]
    public void Withdraw_RejectsAmountThatWouldSpendLockedFunds()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 40m);

        Assert.Throws<InsufficientBalanceException>(() => wallet.Withdraw(61m));

        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(40m, wallet.LockedBalance);
    }

    [Fact]
    public void Deduct_RejectsAmountThatWouldSpendLockedFunds()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 40m);

        Assert.Throws<InsufficientBalanceException>(() => wallet.Deduct(61m));

        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(40m, wallet.LockedBalance);
    }

    [Fact]
    public void LockFunds_ReducesAvailableBalance()
    {
        var wallet = CreateWallet(balance: 100m);

        wallet.LockFunds(30m);

        Assert.Equal(30m, wallet.LockedBalance);
        Assert.Equal(70m, wallet.AvailableBalance);
    }

    [Fact]
    public void ReleaseLockedFunds_RestoresAvailableBalance()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 30m);

        wallet.ReleaseLockedFunds(20m);

        Assert.Equal(10m, wallet.LockedBalance);
        Assert.Equal(90m, wallet.AvailableBalance);
    }

    [Fact]
    public void ReleaseLockedFunds_RejectsAmountGreaterThanLockedBalance()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 30m);

        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.ReleaseLockedFunds(31m));

        Assert.Equal(30m, wallet.LockedBalance);
    }

    [Fact]
    public void CompleteLockedWithdrawal_DeductsBalanceAndLockedBalance()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 30m);

        wallet.CompleteLockedWithdrawal(30m);

        Assert.Equal(70m, wallet.Balance);
        Assert.Equal(0m, wallet.LockedBalance);
        Assert.Equal(70m, wallet.AvailableBalance);
    }

    [Fact]
    public void CompleteLockedWithdrawal_RejectsAmountGreaterThanLockedBalance()
    {
        var wallet = CreateWallet(balance: 100m, lockedBalance: 30m);

        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.CompleteLockedWithdrawal(31m));

        Assert.Equal(100m, wallet.Balance);
        Assert.Equal(30m, wallet.LockedBalance);
    }

    private static WalletAccount CreateWallet(decimal balance, decimal lockedBalance = 0m)
    {
        return new WalletAccount
        {
            UserId = Guid.NewGuid(),
            Currency = CurrencyCode.Vnd,
            Balance = balance,
            LockedBalance = lockedBalance
        };
    }
}
