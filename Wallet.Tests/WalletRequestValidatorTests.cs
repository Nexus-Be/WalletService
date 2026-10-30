using System.ComponentModel.DataAnnotations;
using Wallet.Application.Services.Admin;
using Wallet.Application.Services.Wallet;
using Xunit;

namespace Wallet.Tests;

public class WalletRequestValidatorTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void TopupVnPayRequest_AcceptsValidAmount()
    {
        var request = new TopupVnPayRequest(Amount: 50_000m);
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(9_999)]
    [InlineData(100_000_001)]
    public void TopupVnPayRequest_RejectsAmountOutsideAllowedRange(decimal amount)
    {
        var request = new TopupVnPayRequest(Amount: amount);
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void WithdrawalRequest_AcceptsValidData()
    {
        var request = new WithdrawalRequest(Amount: 100_000m, BankCode: "VCB", AccountNumber: "1234567890");
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void WithdrawalRequest_RejectsAmountBelowMinimum()
    {
        var request = new WithdrawalRequest(Amount: 49_999m, BankCode: "VCB", AccountNumber: "1234567890");
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void TransferRequest_RequiresRecipientUserId()
    {
        var request = new TransferRequest(RecipientUserId: "", Amount: 10_000m);
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void BalanceAdjustmentRequest_AcceptsValidData()
    {
        var request = new BalanceAdjustmentRequest(Amount: 50_000m, AdjustmentType: "Credit", Reason: "Manual Correction");
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void ProcessWithdrawalRequest_RequiresAction()
    {
        var request = new ProcessWithdrawalRequest(Action: "");
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }
}
