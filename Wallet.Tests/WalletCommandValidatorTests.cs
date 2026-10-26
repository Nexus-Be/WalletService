using Wallet.Application.Features.Commands.Topups;
using Wallet.Application.Features.Commands.WalletOperations;
using Xunit;

namespace Wallet.Tests;

public class WalletCommandValidatorTests
{
    private static readonly string ValidUserId = Guid.NewGuid().ToString();
    private static readonly string ValidAdminId = Guid.NewGuid().ToString();

    [Fact]
    public void VnPayTopupValidator_AcceptsValidRequest()
    {
        var command = new InitiateVnPayTopupCommand(
            ValidUserId,
            10_000m,
            "127.0.0.1",
            ReturnUrl: "https://wallet.example.test/return");

        var result = new InitiateVnPayTopupCommandValidator().Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(9_999)]
    [InlineData(100_000_001)]
    public void VnPayTopupValidator_RejectsAmountOutsideAllowedRange(decimal amount)
    {
        var command = new InitiateVnPayTopupCommand(ValidUserId, amount, "127.0.0.1");

        var result = new InitiateVnPayTopupCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Amount));
    }

    [Fact]
    public void VnPayTopupValidator_RejectsNonHttpsReturnUrl()
    {
        var command = new InitiateVnPayTopupCommand(
            ValidUserId,
            10_000m,
            "127.0.0.1",
            ReturnUrl: "http://wallet.example.test/return");

        var result = new InitiateVnPayTopupCommandValidator().Validate(command);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.ReturnUrl));
    }

    [Fact]
    public void WithdrawalValidator_RejectsAmountBelowMinimum()
    {
        var command = new RequestWithdrawalCommand(
            ValidUserId,
            49_999m,
            "Example Bank",
            "123456789",
            "Wallet User");

        var result = new RequestWithdrawalCommandValidator().Validate(command);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Amount));
    }

    [Fact]
    public void BalanceAdjustmentValidator_AcceptsCreditAndDebitActionsCaseInsensitively()
    {
        var validator = new AdjustWalletBalanceCommandValidator();
        var credit = new AdjustWalletBalanceCommand(ValidUserId, ValidAdminId, 10m, "credit", "Correction");
        var debit = new AdjustWalletBalanceCommand(ValidUserId, ValidAdminId, 10m, "DEBIT", "Correction");

        Assert.True(validator.Validate(credit).IsValid);
        Assert.True(validator.Validate(debit).IsValid);
    }

    [Fact]
    public void BalanceAdjustmentValidator_RejectsUnsupportedAction()
    {
        var command = new AdjustWalletBalanceCommand(
            ValidUserId,
            ValidAdminId,
            10m,
            "TRANSFER",
            "Correction");

        var result = new AdjustWalletBalanceCommandValidator().Validate(command);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.Action));
    }
}
