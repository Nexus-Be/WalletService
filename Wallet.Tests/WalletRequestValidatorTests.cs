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
using Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;
using Wallet.Application.Features.VnPay.Queries.ProcessVnPayReturn;
using Wallet.Application.Features.Wallets.Commands.EnsureWallet;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
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
        var request = new TopupVnPayCommand(Amount: 50_000m) { UserId = Guid.NewGuid().ToString() };
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(50_000.001)]
    [InlineData(100_000_001)]
    public void TopupVnPayCommand_RejectsUnsupportedAmountPrecisionOrRange(decimal amount)
    {
        var request = new TopupVnPayCommand(amount) { UserId = Guid.NewGuid().ToString() };

        var results = ValidateModel(request);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Amount)));
    }

    [Theory]
    [InlineData(9_999)]
    [InlineData(100_000_001)]
    public void TopupVnPayCommand_RejectsAmountOutsideAllowedRange(decimal amount)
    {
        var request = new TopupVnPayCommand(Amount: amount) { UserId = Guid.NewGuid().ToString() };
        var results = ValidateModel(request);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Amount)));
    }

    [Fact]
    public void RequestWithdrawalCommand_AcceptsValidData()
    {
        var request = new RequestWithdrawalCommand(
            Amount: 100_000m,
            BankCode: "VCB",
            AccountNumber: "1234567890",
            AccountHolderName: "Test User")
        {
            UserId = Guid.NewGuid().ToString()
        };
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void RequestWithdrawalCommand_RequiresAccountHolderName()
    {
        var request = new RequestWithdrawalCommand(Amount: 100_000m, BankCode: "VCB", AccountNumber: "1234567890");
        var results = ValidateModel(request);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.AccountHolderName)));
    }

    [Fact]
    public void RequestWithdrawalCommand_RejectsAmountBelowMinimum()
    {
        var request = new RequestWithdrawalCommand(
            Amount: 49_999m,
            BankCode: "VCB",
            AccountNumber: "1234567890",
            AccountHolderName: "Test User")
        {
            UserId = Guid.NewGuid().ToString()
        };
        var results = ValidateModel(request);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Amount)));
    }

    [Fact]
    public void RequestWithdrawalCommand_RejectsUnsupportedCurrency()
    {
        var request = new RequestWithdrawalCommand(
            Amount: 100_000m,
            Currency: "USD",
            BankCode: "VCB",
            AccountNumber: "1234567890",
            AccountHolderName: "Test User")
        {
            UserId = Guid.NewGuid().ToString()
        };
        var results = ValidateModel(request);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Currency)));
    }

    [Fact]
    public void TransferCommand_RequiresRecipientUserId()
    {
        var request = new TransferCommand(RecipientUserId: "", Amount: 10_000m)
        {
            SenderUserId = Guid.NewGuid().ToString()
        };
        var results = ValidateModel(request);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.RecipientUserId)));
    }

    [Fact]
    public void AdjustWalletBalanceCommand_AcceptsValidData()
    {
        var request =
            new AdjustWalletBalanceCommand(Amount: 50_000m, AdjustmentType: "Credit", Reason: "Manual Correction")
            {
                WalletId = Guid.NewGuid(),
                AdminUserId = Guid.NewGuid().ToString()
            };
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void ProcessWithdrawalCommand_RequiresAction()
    {
        var request = new ProcessWithdrawalCommand(Action: "")
        {
            WithdrawalId = Guid.NewGuid(),
            AdminUserId = Guid.NewGuid().ToString()
        };
        var results = ValidateModel(request);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Action)));
    }

    [Fact]
    public void ExistingRequests_AcceptValidBoundaries()
    {
        var userId = Guid.NewGuid().ToString();
        var adminId = Guid.NewGuid().ToString();
        var transactionsQuery = new ListTransactionsQuery { UserId = userId };
        Assert.Equal(1, transactionsQuery.Page);
        Assert.Equal(10, transactionsQuery.PageSize);

        var requests = new object[]
        {
            new AdjustWalletBalanceCommand(50_000m, "Credit", "Manual correction")
            {
                WalletId = Guid.NewGuid(),
                AdminUserId = adminId
            },
            new ProcessWithdrawalCommand("Approve")
            {
                WithdrawalId = Guid.NewGuid(),
                AdminUserId = adminId
            },
            new CheckoutDirectCommand(
                Guid.NewGuid().ToString(),
                50_000m,
                ReturnUrl: "https://shop.example/return")
            {
                UserId = userId
            },
            new TopupVnPayCommand(50_000m) { UserId = userId },
            new TransferCommand(Guid.NewGuid().ToString(), 10_000m) { SenderUserId = userId },
            new ProcessVnPayIpnCommand(new Dictionary<string, string>()),
            new ProcessVnPayReturnQuery(new Dictionary<string, string>()),
            new EnsureWalletCommand(Guid.NewGuid()),
            new GetMyWalletQuery(userId),
            transactionsQuery,
            new RequestWithdrawalCommand(
                100_000m,
                BankCode: "VCB",
                AccountNumber: "1234567890",
                AccountHolderName: "Test User") { UserId = userId }
        };

        foreach (var request in requests)
        {
            Assert.Empty(ValidateModel(request));
        }
    }

    [Fact]
    public void ExistingRequests_RejectInvalidBoundaries()
    {
        var requests = new object[]
        {
            new AdjustWalletBalanceCommand(1.001m, "Cash", "Correction")
                { WalletId = Guid.Empty, AdminUserId = "not-a-guid" },
            new ProcessWithdrawalCommand("1")
                { WithdrawalId = Guid.Empty, AdminUserId = "not-a-guid" },
            new CheckoutDirectCommand(
                    "not-a-guid",
                    10.5m,
                    ReturnUrl: "http://shop.example/return")
                { UserId = "not-a-guid" },
            new TopupVnPayCommand(9_999m, Currency: "USD", PaymentMethod: "CARD")
                { UserId = "not-a-guid" },
            new TransferCommand("not-a-guid", 999m) { SenderUserId = "not-a-guid" },
            new ProcessVnPayIpnCommand(null!),
            new ProcessVnPayReturnQuery(null!),
            new EnsureWalletCommand(Guid.Empty),
            new GetMyWalletQuery("not-a-guid"),
            new ListTransactionsQuery(
                    Page: 0,
                    PageSize: 101,
                    Type: "UNKNOWN",
                    FromDate: DateTimeOffset.UtcNow,
                    ToDate: DateTimeOffset.UtcNow.AddDays(-1))
                { UserId = "not-a-guid" },
            new RequestWithdrawalCommand(
                    49_999m,
                    Currency: "USD",
                    BankCode: new string('B', 151),
                    AccountNumber: new string('1', 101),
                    AccountHolderName: new string('A', 256))
                { UserId = "not-a-guid" }
        };

        foreach (var request in requests)
        {
            Assert.NotEmpty(ValidateModel(request));
        }
    }

    [Fact]
    public void CheckoutDirectCommand_RejectsFractionalVndAmount()
    {
        var request = new CheckoutDirectCommand(
            Guid.NewGuid().ToString(),
            10.5m,
            ReturnUrl: "https://shop.example/return")
        {
            UserId = Guid.NewGuid().ToString()
        };

        var results = ValidateModel(request);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Amount)));
    }

    [Fact]
    public void ListTransactionsQuery_RejectsInvalidTypeAndDateRange()
    {
        var request = new ListTransactionsQuery(
            Type: "UNKNOWN",
            FromDate: DateTimeOffset.UtcNow,
            ToDate: DateTimeOffset.UtcNow.AddDays(-1))
        {
            UserId = Guid.NewGuid().ToString()
        };

        var results = ValidateModel(request);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.Type)));
        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.FromDate)) &&
            result.MemberNames.Contains(nameof(request.ToDate)));
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
        var propertiesWithValidation = commandType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttributes<ValidationAttribute>(inherit: false).Any())
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithValidation);
    }
}