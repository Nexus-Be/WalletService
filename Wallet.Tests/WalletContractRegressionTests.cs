namespace Wallet.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;
using WithdrawalEntity = Wallet.Domain.Entities.WithdrawalRequest;

public class WalletContractRegressionTests
{
    private static WalletAccount AddWallet(
        TestWalletDbContext context,
        decimal balance,
        decimal lockedBalance = 0m,
        string status = "ACTIVE")
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = balance,
            LockedBalance = lockedBalance,
            Currency = CurrencyCode.Vnd,
            Status = status
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static WithdrawalEntity AddWithdrawal(TestWalletDbContext context, WalletAccount wallet, decimal amount)
    {
        var withdrawal = new WithdrawalEntity
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = amount,
            Fee = 0m,
            NetAmount = amount,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Test User",
            Status = WithdrawalStatus.Pending
        };
        context.WithdrawalRequests.Add(withdrawal);
        context.SaveChanges();
        return withdrawal;
    }

    [Theory]
    [InlineData("ACTIVE", "Active")]
    [InlineData("SUSPENDED", "Suspended")]
    [InlineData("PENDING_VERIFICATION", "PendingVerification")]
    public void StatusText_NormalizesToPascalCase(string stored, string expected)
    {
        Assert.Equal(expected, stored.ToApiStatus());
    }

    [Fact]
    public async Task GetWallet_Missing_CreatesWalletWithPascalCaseStatusAndAudit()
    {
        using var context = new TestWalletDbContext(nameof(GetWallet_Missing_CreatesWalletWithPascalCaseStatusAndAudit));
        var userId = Guid.NewGuid();
        var handler = new GetMyWalletQueryHandler(context, new TestSender(context));

        var result = await handler.Handle(new GetMyWalletQuery(userId.ToString()), default);

        Assert.Equal("Active", result.Status);
        Assert.Equal(0m, result.Balance);
        Assert.Single(context.Wallets.Where(w => w.UserId == userId));
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WalletCreate));
    }

    [Fact]
    public async Task ProcessWithdrawal_Approve_ReturnsPascalCaseStatus()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_Approve_ReturnsPascalCaseStatus));
        var wallet = AddWallet(context, balance: 500_000m, lockedBalance: 100_000m);
        var withdrawal = AddWithdrawal(context, wallet, 100_000m);

        var handler = new ProcessWithdrawalCommandHandler(context);
        var result = await handler.Handle(
            new ProcessWithdrawalCommand("approve")
            {
                WithdrawalId = withdrawal.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default);

        Assert.Equal("Approved", result.Status);
    }

    [Fact]
    public async Task ProcessWithdrawal_UnknownAction_IsRejectedWithoutSideEffects()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_UnknownAction_IsRejectedWithoutSideEffects));
        var wallet = AddWallet(context, balance: 500_000m, lockedBalance: 100_000m);
        var withdrawal = AddWithdrawal(context, wallet, 100_000m);

        var handler = new ProcessWithdrawalCommandHandler(context);
        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new ProcessWithdrawalCommand("HACK")
            {
                WithdrawalId = withdrawal.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default));

        Assert.Equal("VAL_INVALID_ACTION", exception.ErrorCode);
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(WithdrawalStatus.Pending, withdrawal.Status);
        Assert.Equal(100_000m, wallet.LockedBalance);
    }

    [Fact]
    public async Task AdjustBalance_UnknownType_IsRejectedWithoutSideEffects()
    {
        using var context = new TestWalletDbContext(nameof(AdjustBalance_UnknownType_IsRejectedWithoutSideEffects));
        var wallet = AddWallet(context, balance: 200_000m);

        var transactionsBefore = context.WalletTransactions.Count();
        var handler = new AdjustWalletBalanceCommandHandler(context);
        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new AdjustWalletBalanceCommand(Amount: 50_000m, AdjustmentType: "HACK", Reason: "Typos")
            {
                WalletId = wallet.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default));

        Assert.Equal("VAL_INVALID_ADJUSTMENT_TYPE", exception.ErrorCode);
        Assert.Equal(200_000m, wallet.Balance);
        Assert.Equal(transactionsBefore, context.WalletTransactions.Count());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    public async Task AdjustBalance_NumericType_IsRejected(string adjustmentType)
    {
        using var context = new TestWalletDbContext($"{nameof(AdjustBalance_NumericType_IsRejected)}-{adjustmentType}");
        var wallet = AddWallet(context, balance: 200_000m);

        var handler = new AdjustWalletBalanceCommandHandler(context);

        await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new AdjustWalletBalanceCommand(Amount: 50_000m, AdjustmentType: adjustmentType, Reason: "Numeric")
            {
                WalletId = wallet.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default));

        Assert.Equal(200_000m, wallet.Balance);
    }

    [Fact]
    public async Task Checkout_MalformedOrderId_Returns400ValidationError()
    {
        using var context = new TestWalletDbContext(nameof(Checkout_MalformedOrderId_Returns400ValidationError));
        var wallet = AddWallet(context, balance: 500_000m);
        var transactionsBefore = context.WalletTransactions.Count();
        var handler = new CheckoutDirectCommandHandler(context, new FakeDirectPaymentGateway());

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new CheckoutDirectCommand("not-a-guid", 80_000m, "VNPAY", null, "https://return.test")
            {
                UserId = wallet.UserId.ToString()
            },
            default));

        Assert.Equal("VAL_INVALID_ORDER_ID", exception.ErrorCode);
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(transactionsBefore, context.WalletTransactions.Count());
    }

    [Fact]
    public async Task Checkout_NewOrder_ReturnsPascalCaseStatus()
    {
        using var context = new TestWalletDbContext(nameof(Checkout_NewOrder_ReturnsPascalCaseStatus));
        var wallet = AddWallet(context, balance: 500_000m);
        var handler = new CheckoutDirectCommandHandler(context, new FakeDirectPaymentGateway());

        var result = await handler.Handle(
            new CheckoutDirectCommand(Guid.NewGuid().ToString(), 80_000m, "VNPAY", null, "https://return.test")
            {
                UserId = wallet.UserId.ToString()
            },
            default);

        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task ProcessVnPayIpn_UnknownTopup_Returns01()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_UnknownTopup_Returns01));
        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

        var result = await handler.Handle(
            new ProcessVnPayIpnCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = "TOPUP-UNKNOWN",
                ["vnp_ResponseCode"] = "00",
                ["vnp_Amount"] = "1000000"
            }),
            default);

        Assert.Equal("01", result.ResponseCode);
    }

    [Fact]
    public async Task ProcessVnPayIpn_MissingWallet_DoesNotMarkTopupSuccessful()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_MissingWallet_DoesNotMarkTopupSuccessful));
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = Guid.NewGuid(),
            VnpTxnRef = "TOPUP-MISSING-WALLET",
            Amount = 50_000m,
            Status = TopupStatus.Pending
        };
        context.WalletTopups.Add(topup);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

        var result = await handler.Handle(
            new ProcessVnPayIpnCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = "TOPUP-MISSING-WALLET",
                ["vnp_ResponseCode"] = "00",
                ["vnp_Amount"] = "5000000"
            }),
            default);

        Assert.Equal("01", result.ResponseCode);
        Assert.Equal(TopupStatus.Pending, context.WalletTopups.Single(t => t.VnpTxnRef == "TOPUP-MISSING-WALLET").Status);
        Assert.DoesNotContain(context.WalletTransactions, t => t.ReferenceId == topup.Id);
    }

    [Fact]
    public async Task ProcessVnPayIpn_AmountMismatch_Returns04AndKeepsTopupPending()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_AmountMismatch_Returns04AndKeepsTopupPending));
        var wallet = AddWallet(context, balance: 0m);
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            VnpTxnRef = "TOPUP-MISMATCH",
            Amount = 50_000m,
            Status = TopupStatus.Pending
        };
        context.WalletTopups.Add(topup);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

        var result = await handler.Handle(
            new ProcessVnPayIpnCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = "TOPUP-MISMATCH",
                ["vnp_ResponseCode"] = "00",
                ["vnp_Amount"] = "1"
            }),
            default);

        Assert.Equal("04", result.ResponseCode);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Equal(0m, wallet.Balance);
    }

    [Fact]
    public async Task ProcessVnPayIpn_FailedPayment_MarksTopupFailedAndAudits()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_FailedPayment_MarksTopupFailedAndAudits));
        var wallet = AddWallet(context, balance: 0m);
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            VnpTxnRef = "TOPUP-FAILED",
            Amount = 50_000m,
            Status = TopupStatus.Pending
        };
        context.WalletTopups.Add(topup);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

        var result = await handler.Handle(
            new ProcessVnPayIpnCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = "TOPUP-FAILED",
                ["vnp_ResponseCode"] = "24",
                ["vnp_Amount"] = "5000000"
            }),
            default);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal(TopupStatus.Failed, topup.Status);
        Assert.Equal(0m, wallet.Balance);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.TopupFail));
    }

    [Fact]
    public async Task ListTransactions_InvalidTypeAlias_IsRejected()
    {
        using var context = new TestWalletDbContext(nameof(ListTransactions_InvalidTypeAlias_IsRejected));
        var wallet = AddWallet(context, balance: 0m);
        var handler = new ListTransactionsQueryHandler(context);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new ListTransactionsQuery(Type: "NOT_A_TYPE") { UserId = wallet.UserId.ToString() },
            default));
    }

    [Fact]
    public async Task ListTransactions_DateRange_FiltersRows()
    {
        using var context = new TestWalletDbContext(nameof(ListTransactions_DateRange_FiltersRows));
        var wallet = AddWallet(context, balance: 0m);
        var userId = wallet.UserId.ToString();
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        context.WalletTransactions.AddRange(
            NewTransaction(wallet, baseTime, userId),
            NewTransaction(wallet, baseTime.AddDays(10), userId),
            NewTransaction(wallet, baseTime.AddDays(20), userId));
        context.SaveChanges();

        var handler = new ListTransactionsQueryHandler(context);
        var result = await handler.Handle(
            new ListTransactionsQuery(
                FromDate: baseTime.AddDays(5),
                ToDate: baseTime.AddDays(15))
            {
                UserId = userId
            },
            default);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task ListTransactions_ExcludesSoftDeletedRows()
    {
        using var context = new TestWalletDbContext(nameof(ListTransactions_ExcludesSoftDeletedRows));
        var wallet = AddWallet(context, balance: 0m);
        var userId = wallet.UserId.ToString();
        var deleted = NewTransaction(wallet, DateTimeOffset.UtcNow, userId);
        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTimeOffset.UtcNow;
        context.WalletTransactions.Add(deleted);
        context.SaveChanges();

        var handler = new ListTransactionsQueryHandler(context);
        var result = await handler.Handle(new ListTransactionsQuery { UserId = userId }, default);

        Assert.Empty(result.Items);
    }

    private static WalletTransaction NewTransaction(WalletAccount wallet, DateTimeOffset createdAt, string userId)
        => new()
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = userId,
            Type = WalletTransactionType.TopupVnpay,
            Amount = 10_000m,
            Currency = "Vnd",
            BalanceBefore = wallet.Balance,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            CreatedAt = createdAt
        };
}
