namespace Wallet.Tests;

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;
using WithdrawalEntity = Wallet.Domain.Entities.WithdrawalRequest;

public class WalletOperationsEdgeCaseTests
{
    private static WalletAccount AddWallet(
        TestWalletDbContext context,
        decimal balance,
        decimal lockedBalance = 0m,
        CurrencyCode currency = CurrencyCode.Vnd)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = balance,
            LockedBalance = lockedBalance,
            Currency = currency,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static List<WalletTransaction> TransactionsOf(TestWalletDbContext context, WalletAccount wallet) =>
        context.WalletTransactions.Where(t => t.WalletId == wallet.Id).ToList();

    private static WithdrawalEntity AddWithdrawal(
        TestWalletDbContext context,
        WalletAccount wallet,
        decimal amount,
        WithdrawalStatus status = WithdrawalStatus.Pending)
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
            Status = status
        };
        context.WithdrawalRequests.Add(withdrawal);
        context.SaveChanges();
        return withdrawal;
    }

    // ---- Transfers ------------------------------------------------------------

    private static TransferCommand Transfer(WalletAccount from, WalletAccount to, decimal amount, string? note = null) =>
        new(to.UserId.ToString(), amount, note) { SenderUserId = from.UserId.ToString() };

    [Fact]
    public async Task Transfer_RecordsBothLedgerEntriesWithConsistentBalances()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_RecordsBothLedgerEntriesWithConsistentBalances));
        var sender = AddWallet(context, 300_000m);
        var recipient = AddWallet(context, 20_000m);

        var result = await new TransferCommandHandler(context).Handle(Transfer(sender, recipient, 100_000m, "  rent  "), default);

        var outgoing = Assert.Single(TransactionsOf(context, sender));
        var incoming = Assert.Single(TransactionsOf(context, recipient));
        Assert.Equal(WalletTransactionType.TransferOut, outgoing.Type);
        Assert.Equal(300_000m, outgoing.BalanceBefore);
        Assert.Equal(200_000m, outgoing.BalanceAfter);
        Assert.Equal(WalletTransactionType.TransferIn, incoming.Type);
        Assert.Equal(20_000m, incoming.BalanceBefore);
        Assert.Equal(120_000m, incoming.BalanceAfter);
        Assert.Equal("rent", outgoing.Note);
        Assert.Equal(outgoing.Id.ToString(), result.TransactionId);
        Assert.Equal("Success", result.Status);
    }

    [Fact]
    public async Task Transfer_TotalMoneyIsConserved()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_TotalMoneyIsConserved));
        var a = AddWallet(context, 250_000m);
        var b = AddWallet(context, 75_000m);
        var handler = new TransferCommandHandler(context);

        await handler.Handle(Transfer(a, b, 40_000m), default);
        await handler.Handle(Transfer(b, a, 15_000m), default);

        Assert.Equal(325_000m, a.Balance + b.Balance);
    }

    [Fact]
    public async Task Transfer_ExactBalance_DrainsSenderToZero()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_ExactBalance_DrainsSenderToZero));
        var sender = AddWallet(context, 50_000m);
        var recipient = AddWallet(context, 0m);

        await new TransferCommandHandler(context).Handle(Transfer(sender, recipient, 50_000m), default);

        Assert.Equal(0m, sender.Balance);
        Assert.Equal(50_000m, recipient.Balance);
    }

    [Fact]
    public async Task Transfer_InsufficientBalance_LeavesBothWalletsAndLedgerUntouched()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_InsufficientBalance_LeavesBothWalletsAndLedgerUntouched));
        var sender = AddWallet(context, 10_000m);
        var recipient = AddWallet(context, 5_000m);

        var exception = await Assert.ThrowsAsync<InsufficientBalanceException>(
            () => new TransferCommandHandler(context).Handle(Transfer(sender, recipient, 10_001m), default));

        Assert.Equal("INSUFFICIENT_BALANCE", exception.ErrorCode);
        Assert.Equal(10_000m, sender.Balance);
        Assert.Equal(5_000m, recipient.Balance);
        Assert.Empty(TransactionsOf(context, sender));
        Assert.Empty(TransactionsOf(context, recipient));
    }

    [Fact]
    public async Task Transfer_CannotSpendLockedFunds()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_CannotSpendLockedFunds));
        var sender = AddWallet(context, 100_000m, lockedBalance: 90_000m);
        var recipient = AddWallet(context, 0m);

        await Assert.ThrowsAsync<InsufficientBalanceException>(
            () => new TransferCommandHandler(context).Handle(Transfer(sender, recipient, 20_000m), default));

        Assert.Equal(100_000m, sender.Balance);
    }

    [Fact]
    public async Task Transfer_ToSelf_IsRejected()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_ToSelf_IsRejected));
        var wallet = AddWallet(context, 100_000m);

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => new TransferCommandHandler(context).Handle(Transfer(wallet, wallet, 10_000m), default));

        Assert.Equal("Cannot transfer funds to yourself.", exception.Message);
        Assert.Equal(100_000m, wallet.Balance);
    }

    [Fact]
    public async Task Transfer_UnknownRecipient_ThrowsWalletNotFoundAndKeepsSenderBalance()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_UnknownRecipient_ThrowsWalletNotFoundAndKeepsSenderBalance));
        var sender = AddWallet(context, 100_000m);
        var command = new TransferCommand(Guid.NewGuid().ToString(), 10_000m) { SenderUserId = sender.UserId.ToString() };

        await Assert.ThrowsAsync<WalletNotFoundException>(() => new TransferCommandHandler(context).Handle(command, default));

        Assert.Equal(100_000m, sender.Balance);
        Assert.Empty(TransactionsOf(context, sender));
    }

    [Fact]
    public async Task Transfer_UnknownSender_ThrowsWalletNotFound()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_UnknownSender_ThrowsWalletNotFound));
        var recipient = AddWallet(context, 0m);
        var command = new TransferCommand(recipient.UserId.ToString(), 10_000m) { SenderUserId = Guid.NewGuid().ToString() };

        await Assert.ThrowsAsync<WalletNotFoundException>(() => new TransferCommandHandler(context).Handle(command, default));

        Assert.Equal(0m, recipient.Balance);
    }

    [Theory]
    [InlineData("", "recipient")]
    [InlineData("not-a-guid", "recipient")]
    [InlineData("recipient", "not-a-guid")]
    public async Task Transfer_MalformedUserIds_AreRejectedAsArgumentErrors(string sender, string recipient)
    {
        using var context = new TestWalletDbContext(nameof(Transfer_MalformedUserIds_AreRejectedAsArgumentErrors) + sender + recipient);
        var valid = Guid.NewGuid().ToString();
        var command = new TransferCommand(recipient == "recipient" ? valid : recipient, 10_000m)
        {
            SenderUserId = sender == "recipient" ? valid : sender
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TransferCommandHandler(context).Handle(command, default));
    }

    [Fact]
    public async Task Transfer_CurrencyMismatch_IsRejected()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_CurrencyMismatch_IsRejected));
        var sender = AddWallet(context, 100_000m, currency: CurrencyCode.Vnd);
        var recipient = AddWallet(context, 0m, currency: CurrencyCode.Usd);

        await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => new TransferCommandHandler(context).Handle(Transfer(sender, recipient, 10_000m), default));

        Assert.Equal(100_000m, sender.Balance);
        Assert.Equal(0m, recipient.Balance);
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1_000, true)]
    [InlineData(100_000_000, true)]
    [InlineData(100_000_001, false)]
    public void Transfer_AmountBoundaries_AreValidated(int amount, bool valid)
    {
        var results = new List<ValidationResult>();

        var isValid = RequestValidation.TryValidate(
            new TransferCommand(Guid.NewGuid().ToString(), amount) { SenderUserId = Guid.NewGuid().ToString() },
            results);

        Assert.Equal(valid, isValid);
    }

    // ---- Admin balance adjustments ----------------------------------------------

    private static AdjustWalletBalanceCommand Adjust(WalletAccount wallet, decimal amount, string type) =>
        new(amount, type, "  support ticket 42  ")
        {
            UserId = wallet.UserId,
            AdminUserId = Guid.NewGuid().ToString()
        };

    [Theory]
    [InlineData("Credit")]
    [InlineData("credit")]
    [InlineData("CREDIT")]
    public async Task AdjustBalance_Credit_IncreasesBalanceAndWritesAudit(string type)
    {
        using var context = new TestWalletDbContext(nameof(AdjustBalance_Credit_IncreasesBalanceAndWritesAudit) + type);
        var wallet = AddWallet(context, 100_000m);

        var result = await new AdjustWalletBalanceCommandHandler(context).Handle(Adjust(wallet, 25_000m, type), default);

        Assert.Equal(125_000m, result.BalanceAfter);
        var transaction = Assert.Single(TransactionsOf(context, wallet));
        Assert.Equal(WalletTransactionType.AdjustmentCredit, transaction.Type);
        Assert.Equal(100_000m, transaction.BalanceBefore);
        Assert.Contains("support ticket 42", transaction.Note);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.BalanceAdjustment && a.EntityId == wallet.Id.ToString()));
    }

    [Fact]
    public async Task AdjustBalance_DebitBeyondBalance_IsRejectedWithoutChange()
    {
        using var context = new TestWalletDbContext(nameof(AdjustBalance_DebitBeyondBalance_IsRejectedWithoutChange));
        var wallet = AddWallet(context, 10_000m);

        await Assert.ThrowsAsync<InsufficientBalanceException>(
            () => new AdjustWalletBalanceCommandHandler(context).Handle(Adjust(wallet, 10_001m, "Debit"), default));

        Assert.Equal(10_000m, wallet.Balance);
        Assert.Empty(TransactionsOf(context, wallet));
        Assert.Empty(context.AuditLogs.Where(a => a.EntityId == wallet.Id.ToString()));
    }

    [Fact]
    public async Task AdjustBalance_UnknownWallet_ThrowsWalletNotFound()
    {
        using var context = new TestWalletDbContext(nameof(AdjustBalance_UnknownWallet_ThrowsWalletNotFound));
        var command = new AdjustWalletBalanceCommand(1_000m, "Credit", "reason")
        {
            UserId = Guid.NewGuid(),
            AdminUserId = Guid.NewGuid().ToString()
        };

        await Assert.ThrowsAsync<WalletNotFoundException>(
            () => new AdjustWalletBalanceCommandHandler(context).Handle(command, default));
    }

    [Fact]
    public async Task AdjustBalance_InvalidAdminId_IsRejected()
    {
        using var context = new TestWalletDbContext(nameof(AdjustBalance_InvalidAdminId_IsRejected));
        var wallet = AddWallet(context, 100m);
        var command = Adjust(wallet, 10m, "Credit") with { AdminUserId = "nope" };

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new AdjustWalletBalanceCommandHandler(context).Handle(command, default));

        Assert.Equal(100m, wallet.Balance);
    }

    // ---- Withdrawal processing ------------------------------------------------

    private static ProcessWithdrawalCommand Process(WithdrawalEntity withdrawal, string action, string? note = null) =>
        new(action, note)
        {
            WithdrawalId = withdrawal.Id,
            AdminUserId = Guid.NewGuid().ToString()
        };

    [Fact]
    public async Task ProcessWithdrawal_Reject_ReleasesLockedFundsWithoutDebit()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_Reject_ReleasesLockedFundsWithoutDebit));
        var wallet = AddWallet(context, 500_000m, lockedBalance: 100_000m);
        var withdrawal = AddWithdrawal(context, wallet, 100_000m);

        var result = await new ProcessWithdrawalCommandHandler(context)
            .Handle(Process(withdrawal, "reject", "bad account"), default);

        Assert.Equal("Rejected", result.Status);
        Assert.Null(result.TransactionId);
        Assert.Equal(500_000m, wallet.Balance);
        Assert.Equal(0m, wallet.LockedBalance);
        Assert.Empty(TransactionsOf(context, wallet));
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WithdrawalReject));
    }

    [Theory]
    [InlineData("Approve")]
    [InlineData("Reject")]
    public async Task ProcessWithdrawal_AlreadyProcessed_CannotBeProcessedAgain(string action)
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_AlreadyProcessed_CannotBeProcessedAgain) + action);
        var wallet = AddWallet(context, 500_000m, lockedBalance: 100_000m);
        var withdrawal = AddWithdrawal(context, wallet, 100_000m);
        var handler = new ProcessWithdrawalCommandHandler(context);
        await handler.Handle(Process(withdrawal, action), default);
        var balance = wallet.Balance;
        var locked = wallet.LockedBalance;

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => handler.Handle(Process(withdrawal, "Approve"), default));

        Assert.Equal("Withdrawal request has already been processed.", exception.Message);
        Assert.Equal(balance, wallet.Balance);
        Assert.Equal(locked, wallet.LockedBalance);
    }

    [Fact]
    public async Task ProcessWithdrawal_UnknownRequest_ThrowsNotFound()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_UnknownRequest_ThrowsNotFound));
        var command = new ProcessWithdrawalCommand("Approve")
        {
            WithdrawalId = Guid.NewGuid(),
            AdminUserId = Guid.NewGuid().ToString()
        };

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => new ProcessWithdrawalCommandHandler(context).Handle(command, default));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task ProcessWithdrawal_ApproveWithoutLockedFunds_IsRejectedAndKeepsRequestPending()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_ApproveWithoutLockedFunds_IsRejectedAndKeepsRequestPending));
        var wallet = AddWallet(context, 500_000m, lockedBalance: 0m);
        var withdrawal = AddWithdrawal(context, wallet, 100_000m);

        await Assert.ThrowsAnyAsync<Exception>(
            () => new ProcessWithdrawalCommandHandler(context).Handle(Process(withdrawal, "Approve"), default));

        Assert.Equal(500_000m, wallet.Balance);
        Assert.Empty(TransactionsOf(context, wallet));
    }

    // ---- VNPay IPN --------------------------------------------------------------

    private static ProcessVnPayIpnCommandHandler IpnHandler(TestWalletDbContext context) =>
        new(context, new FakeVnPayService(), NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

    private static ProcessVnPayIpnCommand Ipn(string txnRef, string responseCode, string amount) =>
        new(new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = txnRef,
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_Amount"] = amount,
            ["vnp_CurrCode"] = "VND"
        });

    private static WalletTopup AddTopup(TestWalletDbContext context, WalletAccount wallet, string txnRef, decimal amount)
    {
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            VnpTxnRef = txnRef,
            Amount = amount,
            Status = TopupStatus.Pending
        };
        context.WalletTopups.Add(topup);
        context.SaveChanges();
        return topup;
    }

    [Fact]
    public async Task Ipn_ReplayedSuccess_DoesNotCreditTwice()
    {
        using var context = new TestWalletDbContext(nameof(Ipn_ReplayedSuccess_DoesNotCreditTwice));
        var wallet = AddWallet(context, 0m);
        AddTopup(context, wallet, "TOPUP-REPLAY", 50_000m);
        var handler = IpnHandler(context);

        var first = await handler.Handle(Ipn("TOPUP-REPLAY", "00", "5000000"), default);
        var second = await handler.Handle(Ipn("TOPUP-REPLAY", "00", "5000000"), default);

        Assert.Equal("00", first.ResponseCode);
        Assert.Equal("02", second.ResponseCode);
        Assert.Equal(50_000m, wallet.Balance);
        Assert.Single(TransactionsOf(context, wallet));
    }

    [Fact]
    public async Task Ipn_FailureAfterSuccess_DoesNotRevertCredit()
    {
        using var context = new TestWalletDbContext(nameof(Ipn_FailureAfterSuccess_DoesNotRevertCredit));
        var wallet = AddWallet(context, 0m);
        var topup = AddTopup(context, wallet, "TOPUP-LATE-FAIL", 50_000m);
        var handler = IpnHandler(context);

        await handler.Handle(Ipn("TOPUP-LATE-FAIL", "00", "5000000"), default);
        var late = await handler.Handle(Ipn("TOPUP-LATE-FAIL", "24", "5000000"), default);

        Assert.Equal("02", late.ResponseCode);
        Assert.Equal(TopupStatus.Success, topup.Status);
        Assert.Equal(50_000m, wallet.Balance);
    }

    [Theory]
    [InlineData("", "00")]
    [InlineData("TOPUP-X", "")]
    public async Task Ipn_MissingParameters_Returns99(string txnRef, string responseCode)
    {
        using var context = new TestWalletDbContext(nameof(Ipn_MissingParameters_Returns99) + txnRef + responseCode);

        var result = await IpnHandler(context).Handle(Ipn(txnRef, responseCode, "100"), default);

        Assert.Equal("99", result.ResponseCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public async Task Ipn_UnparseableAmount_Returns04AndKeepsTopupPending(string amount)
    {
        using var context = new TestWalletDbContext(nameof(Ipn_UnparseableAmount_Returns04AndKeepsTopupPending) + amount);
        var wallet = AddWallet(context, 0m);
        var topup = AddTopup(context, wallet, "TOPUP-BADAMT-" + amount, 50_000m);

        var result = await IpnHandler(context).Handle(Ipn(Assert.IsType<string>(topup.VnpTxnRef), "00", amount), default);

        Assert.Equal("04", result.ResponseCode);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Equal(0m, wallet.Balance);
    }

    [Fact]
    public async Task Ipn_AmountMismatch_NeverCreditsWallet()
    {
        using var context = new TestWalletDbContext(nameof(Ipn_AmountMismatch_NeverCreditsWallet));
        var wallet = AddWallet(context, 0m);
        AddTopup(context, wallet, "TOPUP-MISMATCH", 50_000m);

        var result = await IpnHandler(context).Handle(Ipn("TOPUP-MISMATCH", "00", "9999999999"), default);

        Assert.Equal("04", result.ResponseCode);
        Assert.Equal(0m, wallet.Balance);
        Assert.Empty(TransactionsOf(context, wallet));
    }
}
