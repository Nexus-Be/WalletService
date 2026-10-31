namespace Wallet.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;
using Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Topups.Commands.TopupVnPay;
using Wallet.Application.Features.Transfers.Commands.Transfer;
using Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;
using Wallet.Application.Features.VnPay.Queries.ProcessVnPayReturn;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Features.Wallets.Queries.ListTransactions;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;
using WithdrawalEntity = Wallet.Domain.Entities.WithdrawalRequest;

public class WalletUseCaseBehaviorTests
{
    private static WalletAccount AddWallet(TestWalletDbContext context, decimal balance, decimal lockedBalance = 0m, Guid? userId = null)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            Balance = balance,
            LockedBalance = lockedBalance,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    [Fact]
    public async Task GetWallet_Existing_ReturnsBalances()
    {
        using var context = new TestWalletDbContext(nameof(GetWallet_Existing_ReturnsBalances));
        var wallet = AddWallet(context, balance: 500_000m, lockedBalance: 50_000m);
        var handler = new GetMyWalletQueryHandler(context, new TestSender(context));

        var result = await handler.Handle(new GetMyWalletQuery(wallet.UserId.ToString()), default);

        Assert.Equal(wallet.Id.ToString(), result.WalletId);
        Assert.Equal(500_000m, result.Balance);
        Assert.Equal(450_000m, result.AvailableBalance);
        Assert.Equal(50_000m, result.LockedBalance);
        Assert.Equal("Vnd", result.Currency);
        Assert.Equal("Active", result.Status);
    }

    [Fact]
    public async Task GetWallet_Missing_CreatesZeroBalanceWallet()
    {
        using var context = new TestWalletDbContext(nameof(GetWallet_Missing_CreatesZeroBalanceWallet));
        var userId = Guid.NewGuid();
        var handler = new GetMyWalletQueryHandler(context, new TestSender(context));

        var result = await handler.Handle(new GetMyWalletQuery(userId.ToString()), default);

        Assert.Equal(userId.ToString(), result.UserId);
        Assert.Equal(0m, result.Balance);
        Assert.Single(context.Wallets.Where(w => w.UserId == userId));
    }

    [Fact]
    public async Task ListTransactions_FiltersByAliasAndPaginatesNewestFirst()
    {
        using var context = new TestWalletDbContext(nameof(ListTransactions_FiltersByAliasAndPaginatesNewestFirst));
        var wallet = AddWallet(context, balance: 100_000m);
        var userId = wallet.UserId.ToString();
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        context.WalletTransactions.AddRange(
            NewTransaction(wallet, WalletTransactionType.TopupVnpay, 10_000m, baseTime.AddMinutes(1), userId),
            NewTransaction(wallet, WalletTransactionType.TopupVnpay, 20_000m, baseTime.AddMinutes(2), userId),
            NewTransaction(wallet, WalletTransactionType.PurchaseDebit, 30_000m, baseTime.AddMinutes(3), userId));
        context.SaveChanges();

        var handler = new ListTransactionsQueryHandler(context);

        var result = await handler.Handle(
            new ListTransactionsQuery(Page: 1, PageSize: 2, Type: "DEPOSIT") { UserId = userId },
            default);

        Assert.Equal(2, result.Pagination.TotalCount);
        Assert.Equal(1, result.Pagination.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(20_000m, result.Items[0].Amount);
        Assert.Equal(10_000m, result.Items[1].Amount);
        Assert.All(result.Items, item => Assert.Equal("TopupVnpay", item.Type));
    }

    [Fact]
    public async Task TopupVnPay_CreatesPendingTopupAndAudit()
    {
        using var context = new TestWalletDbContext(nameof(TopupVnPay_CreatesPendingTopupAndAudit));
        var wallet = AddWallet(context, balance: 0m);
        var handler = new TopupVnPayCommandHandler(context, new FakeVnPayService());

        var result = await handler.Handle(
            new TopupVnPayCommand(Amount: 50_000m, ReturnUrl: "https://return.test")
            {
                UserId = wallet.UserId.ToString(),
                ClientIp = "127.0.0.1"
            },
            default);

        Assert.Equal(50_000m, result.Amount);
        Assert.Equal("Pending", result.Status);
        Assert.Contains("sandbox.vnpay.vn", result.PaymentUrl);
        Assert.Single(context.WalletTopups.Where(t => t.WalletId == wallet.Id));
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.TopupRequest));
    }

    [Fact]
    public async Task Transfer_MovesFundsOnBothSides()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_MovesFundsOnBothSides));
        var sender = AddWallet(context, balance: 100_000m);
        var recipient = AddWallet(context, balance: 0m);
        var handler = new TransferCommandHandler(context);

        var result = await handler.Handle(
            new TransferCommand(recipient.UserId.ToString(), 25_000m, "rent")
            {
                SenderUserId = sender.UserId.ToString()
            },
            default);

        Assert.Equal(25_000m, result.Amount);
        Assert.Equal(75_000m, sender.Balance);
        Assert.Equal(25_000m, recipient.Balance);

        var transactions = context.WalletTransactions
            .Where(t => t.WalletId == sender.Id || t.WalletId == recipient.Id)
            .ToList();
        Assert.Equal(2, transactions.Count);
        Assert.Contains(transactions, t => t.Type == WalletTransactionType.TransferOut);
        Assert.Contains(transactions, t => t.Type == WalletTransactionType.TransferIn);
    }

    [Fact]
    public async Task Transfer_InsufficientBalance_Throws()
    {
        using var context = new TestWalletDbContext(nameof(Transfer_InsufficientBalance_Throws));
        var sender = AddWallet(context, balance: 10_000m);
        var recipient = AddWallet(context, balance: 0m);
        var handler = new TransferCommandHandler(context);

        await Assert.ThrowsAsync<InsufficientBalanceException>(() => handler.Handle(
            new TransferCommand(recipient.UserId.ToString(), 10_001m) { SenderUserId = sender.UserId.ToString() },
            default));

        Assert.Equal(10_000m, sender.Balance);
        Assert.Equal(0m, recipient.Balance);
    }

    [Fact]
    public async Task CheckoutDirect_ExistingPendingTransaction_IsIdempotent()
    {
        using var context = new TestWalletDbContext(nameof(CheckoutDirect_ExistingPendingTransaction_IsIdempotent));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var existing = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = 100_000m,
            Currency = "Vnd",
            BalanceBefore = wallet.Balance,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Pending,
            ReferenceId = orderId,
            Note = "Payment for order via VNPAY"
        };
        context.WalletTransactions.Add(existing);
        context.SaveChanges();

        var handler = new CheckoutDirectCommandHandler(context, new FakeDirectPaymentGateway());
        var result = await handler.Handle(
            new CheckoutDirectCommand(orderId.ToString(), 100_000m, "VNPAY", null, "https://return.test")
            {
                UserId = wallet.UserId.ToString()
            },
            default);

        Assert.Equal(existing.Id.ToString(), result.TransactionId);
        Assert.Single(context.WalletTransactions.Where(t => t.ReferenceId == orderId));
    }

    [Fact]
    public async Task CheckoutDirect_NewOrder_CreatesPendingTransaction()
    {
        using var context = new TestWalletDbContext(nameof(CheckoutDirect_NewOrder_CreatesPendingTransaction));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var handler = new CheckoutDirectCommandHandler(context, new FakeDirectPaymentGateway());

        var result = await handler.Handle(
            new CheckoutDirectCommand(orderId.ToString(), 80_000m, "VNPAY", null, "https://return.test")
            {
                UserId = wallet.UserId.ToString()
            },
            default);

        Assert.Equal(80_000m, result.Amount);
        Assert.Contains("gateway.test", result.PaymentUrl);
        Assert.Equal(500_000m, wallet.Balance);

        var transaction = Assert.Single(context.WalletTransactions.Where(t => t.ReferenceId == orderId));
        Assert.Equal(TransactionStatus.Pending, transaction.Status);
    }

    [Fact]
    public async Task ProcessVnPayIpn_InvalidSignature_Returns97()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_InvalidSignature_Returns97));
        var vnPay = new FakeVnPayService { SignatureIsValid = false };
        var handler = new ProcessVnPayIpnCommandHandler(context, vnPay, NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

        var result = await handler.Handle(
            new ProcessVnPayIpnCommand(new Dictionary<string, string>(), "127.0.0.1"),
            default);

        Assert.Equal("97", result.ResponseCode);
    }

    [Fact]
    public async Task ProcessVnPayIpn_Success_DepositsAndMarksTopupSuccess()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_Success_DepositsAndMarksTopupSuccess));
        var wallet = AddWallet(context, balance: 0m);
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            VnpTxnRef = "TOPUP-1",
            Amount = 50_000m,
            Status = TopupStatus.Pending
        };
        context.WalletTopups.Add(topup);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "TOPUP-1",
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = "5000000",
            ["vnp_TransactionNo"] = "123456"
        };

        var result = await handler.Handle(new ProcessVnPayIpnCommand(parameters, "127.0.0.1"), default);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal(TopupStatus.Success, topup.Status);
        Assert.Equal(50_000m, wallet.Balance);
        Assert.Single(context.WalletTransactions.Where(t => t.Type == WalletTransactionType.TopupVnpay && t.WalletId == wallet.Id));
    }

    [Fact]
    public async Task ProcessWithdrawal_Approve_DeductsLockedFunds()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_Approve_DeductsLockedFunds));
        var wallet = AddWallet(context, balance: 500_000m, lockedBalance: 100_000m);
        var withdrawal = new WithdrawalEntity
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Amount = 100_000m,
            Fee = 0m,
            NetAmount = 100_000m,
            BankName = "VCB",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "Test User",
            Status = WithdrawalStatus.Pending
        };
        context.WithdrawalRequests.Add(withdrawal);
        context.SaveChanges();

        var handler = new ProcessWithdrawalCommandHandler(context);
        var result = await handler.Handle(
            new ProcessWithdrawalCommand("APPROVE")
            {
                WithdrawalId = withdrawal.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default);

        Assert.Equal("Approved", result.Status);
        Assert.Equal(400_000m, wallet.Balance);
        Assert.Equal(0m, wallet.LockedBalance);
        Assert.Single(context.WalletTransactions.Where(t => t.Type == WalletTransactionType.WithdrawalDebit));
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WithdrawalApprove));
    }

    [Fact]
    public async Task AdjustBalance_Debit_ReducesBalance()
    {
        using var context = new TestWalletDbContext(nameof(AdjustBalance_Debit_ReducesBalance));
        var wallet = AddWallet(context, balance: 200_000m);
        var handler = new AdjustWalletBalanceCommandHandler(context);

        var result = await handler.Handle(
            new AdjustWalletBalanceCommand(Amount: 50_000m, AdjustmentType: "Debit", Reason: "Correction")
            {
                WalletId = wallet.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default);

        Assert.Equal(150_000m, result.BalanceAfter);
        Assert.Equal(150_000m, wallet.Balance);
        Assert.Single(context.WalletTransactions.Where(t => t.Type == WalletTransactionType.AdjustmentDebit));
    }

    [Fact]
    public async Task ProcessVnPayReturn_InvalidSignature_ReturnsFailure()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayReturn_InvalidSignature_ReturnsFailure));
        var vnPay = new FakeVnPayService { SignatureIsValid = false };
        var handler = new ProcessVnPayReturnQueryHandler(vnPay, NullLogger<ProcessVnPayReturnQueryHandler>.Instance);

        var result = await handler.Handle(
            new ProcessVnPayReturnQuery(new Dictionary<string, string>()),
            default);

        Assert.False(result.Success);
        Assert.Equal("97", result.ResponseCode);
    }

    [Fact]
    public async Task RequestWithdrawal_IsDisabled()
    {
        using var context = new TestWalletDbContext(nameof(RequestWithdrawal_IsDisabled));
        var handler = new RequestWithdrawalCommandHandler();

        await Assert.ThrowsAsync<BusinessRuleValidationException>(() => handler.Handle(
            new RequestWithdrawalCommand(Amount: 100_000m, BankCode: "VCB", AccountNumber: "1234567890"),
            default));
    }

    private static WalletTransaction NewTransaction(
        WalletAccount wallet,
        WalletTransactionType type,
        decimal amount,
        DateTimeOffset createdAt,
        string userId)
        => new()
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = userId,
            Type = type,
            Amount = amount,
            Currency = "Vnd",
            BalanceBefore = wallet.Balance,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Success,
            CreatedAt = createdAt
        };
}
