using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Topups.Commands.TopupVnPay;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Xunit;

namespace Wallet.Tests;

public class WalletRequestValidatorTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        RequestValidation.TryValidate(model, results);
        return results;
    }

    [Fact]
    public void TopupVnPayCommand_AcceptsValidAmount()
    {
        var request = new TopupVnPayCommand(Amount: 50_000m);
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(9_999)]
    [InlineData(100_000_001)]
    public void TopupVnPayCommand_RejectsAmountOutsideAllowedRange(decimal amount)
    {
        var request = new TopupVnPayCommand(Amount: amount);
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void RequestWithdrawalCommand_AcceptsValidData()
    {
        var request = new RequestWithdrawalCommand(Amount: 100_000m, BankCode: "VCB", AccountNumber: "1234567890");
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void RequestWithdrawalCommand_RejectsAmountBelowMinimum()
    {
        var request = new RequestWithdrawalCommand(Amount: 49_999m, BankCode: "VCB", AccountNumber: "1234567890");
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void TransferCommand_RequiresRecipientUserId()
    {
        var request = new TransferCommand(RecipientUserId: "", Amount: 10_000m);
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void AdjustWalletBalanceCommand_AcceptsValidData()
    {
        var request = new AdjustWalletBalanceCommand(Amount: 50_000m, AdjustmentType: "Credit", Reason: "Manual Correction");
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void ProcessWithdrawalCommand_RequiresAction()
    {
        var request = new ProcessWithdrawalCommand(Action: "");
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(typeof(TransferCommand))]
    [InlineData(typeof(TopupVnPayCommand))]
    [InlineData(typeof(RequestWithdrawalCommand))]
    [InlineData(typeof(CheckoutDirectCommand))]
    [InlineData(typeof(AdjustWalletBalanceCommand))]
    [InlineData(typeof(ProcessWithdrawalCommand))]
    public void RecordCommands_MustNotHaveValidationMetadataOnProperties(Type commandType)
    {
        // ASP.NET Core ModelMetadata throws InvalidOperationException if validation metadata
        // is placed on properties of record types with primary constructors.
        var propertiesWithValidation = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttributes<ValidationAttribute>(inherit: false).Any())
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithValidation);
    }
}
