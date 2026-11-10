namespace Wallet.Tests;

using System.Security.Cryptography;
using System.Text;
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
    private static WalletAccount AddWallet(TestWalletDbContext context, decimal balance, decimal lockedBalance = 0m,
        Guid? userId = null)
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
        Assert.Equal(
            AuditAction.WalletCreate,
            Assert.Single(context.AuditLogs.Where(log => log.UserId == userId.ToString())).Action);
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
        const string returnUrl = "https://return.test";
        const string transactionReference = "DIRECT-PENDING-1";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            "\n",
            wallet.UserId.ToString("D"),
            orderId.ToString("D"),
            "100000.00",
            "VNPAY",
            string.Empty,
            returnUrl))));
        context.DirectPaymentOrders.Add(new DirectPaymentOrder
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            VnpTxnRef = transactionReference,
            Amount = 100_000m,
            Status = TopupStatus.Pending,
            RequestFingerprint = fingerprint
        });
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

        var vnPay = new FakeVnPayService();
        var handler = new CheckoutDirectCommandHandler(context, vnPay);
        var result = await handler.Handle(
            new CheckoutDirectCommand(orderId.ToString(), 100_000m, null, returnUrl)
            {
                UserId = wallet.UserId.ToString()
            },
            default);

        Assert.Equal(existing.Id.ToString(), result.TransactionId);
        Assert.Equal(transactionReference, vnPay.LastDirectTransactionReference);
        Assert.Single(context.WalletTransactions.Where(t => t.ReferenceId == orderId));
        Assert.Single(context.DirectPaymentOrders.Where(payment => payment.OrderId == orderId));
    }

    [Fact]
    public async Task CheckoutDirect_NewOrder_CreatesPendingVnPayPayment()
    {
        using var context = new TestWalletDbContext(nameof(CheckoutDirect_NewOrder_CreatesPendingVnPayPayment));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var handler = new CheckoutDirectCommandHandler(context, new FakeVnPayService());

        var result = await handler.Handle(
            new CheckoutDirectCommand(orderId.ToString(), 80_000m, null, "https://return.test")
            {
                UserId = wallet.UserId.ToString()
            },
            default);

        Assert.Equal(80_000m, result.Amount);
        Assert.Contains("sandbox.vnpay.vn", result.PaymentUrl);
        Assert.Equal(500_000m, wallet.Balance);

        var transaction = Assert.Single(context.WalletTransactions.Where(t => t.ReferenceId == orderId));
        Assert.Equal(TransactionStatus.Pending, transaction.Status);
        Assert.Single(context.DirectPaymentOrders.Where(payment => payment.OrderId == orderId));
    }

    [Fact]
    public async Task CheckoutDirect_VnPayPersistsIntentAndReusesReferenceOnRetry()
    {
        using var context = new TestWalletDbContext(
            nameof(CheckoutDirect_VnPayPersistsIntentAndReusesReferenceOnRetry));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var vnPay = new FakeVnPayService();
        var handler = new CheckoutDirectCommandHandler(context, vnPay);
        var command = new CheckoutDirectCommand(
            orderId.ToString(),
            80_000m,
            " NCB ",
            "https://return.test")
        {
            UserId = wallet.UserId.ToString(),
            ClientIp = "203.0.113.10"
        };

        var firstResponse = await handler.Handle(command, default);
        var firstReference = vnPay.LastDirectTransactionReference;
        var retryResponse = await handler.Handle(command with { ClientIp = "203.0.113.11" }, default);

        var paymentOrder = Assert.Single(context.DirectPaymentOrders.Where(order => order.OrderId == orderId));
        var transaction = Assert.Single(context.WalletTransactions.Where(t => t.ReferenceId == orderId));
        Assert.Equal(firstResponse.TransactionId, retryResponse.TransactionId);
        Assert.Equal(firstReference, vnPay.LastDirectTransactionReference);
        Assert.Equal(2, vnPay.DirectPaymentUrlCalls);
        Assert.Equal(64, paymentOrder.RequestFingerprint?.Length);
        Assert.Equal(TopupStatus.Pending, paymentOrder.Status);
        Assert.Equal(TransactionStatus.Pending, transaction.Status);
        Assert.Equal(500_000m, wallet.Balance);
    }

    [Fact]
    public async Task CheckoutDirect_VnPayRejectsChangedParametersForExistingOrder()
    {
        using var context = new TestWalletDbContext(
            nameof(CheckoutDirect_VnPayRejectsChangedParametersForExistingOrder));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var handler = new CheckoutDirectCommandHandler(context, new FakeVnPayService());
        var command = new CheckoutDirectCommand(
            orderId.ToString(),
            80_000m,
            null,
            "https://return.test")
        {
            UserId = wallet.UserId.ToString()
        };
        await handler.Handle(command, default);

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            handler.Handle(command with { Amount = 90_000m }, default));

        Assert.Equal("PAYMENT_ORDER_CONFLICT", exception.ErrorCode);
        Assert.Equal(409, exception.StatusCode);
        Assert.Single(context.DirectPaymentOrders.Where(order => order.OrderId == orderId));
        Assert.Single(context.WalletTransactions.Where(t => t.ReferenceId == orderId));
    }

    [Fact]
    public async Task CheckoutDirect_VnPayRequiresVndWholeAmount()
    {
        using var context = new TestWalletDbContext(nameof(CheckoutDirect_VnPayRequiresVndWholeAmount));
        var wallet = AddWallet(context, balance: 500_000m);
        var existingPaymentOrderCount = context.DirectPaymentOrders.Count();
        wallet.Currency = CurrencyCode.Usd;
        context.SaveChanges();
        var handler = new CheckoutDirectCommandHandler(context, new FakeVnPayService());
        var orderId = Guid.NewGuid();
        var command = new CheckoutDirectCommand(
            orderId.ToString(),
            80_000m,
            null,
            "https://return.test")
        {
            UserId = wallet.UserId.ToString()
        };

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            handler.Handle(command, default));

        Assert.Equal("VAL_INVALID_CURRENCY", exception.ErrorCode);
        Assert.Equal(existingPaymentOrderCount, context.DirectPaymentOrders.Count());
    }

    [Fact]
    public async Task CheckoutDirect_VnPayRejectsNonHttpsReturnUrl()
    {
        using var context = new TestWalletDbContext(nameof(CheckoutDirect_VnPayRejectsNonHttpsReturnUrl));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var handler = new CheckoutDirectCommandHandler(context, new FakeVnPayService());
        var command = new CheckoutDirectCommand(
            orderId.ToString(),
            80_000m,
            null,
            "http://return.test")
        {
            UserId = wallet.UserId.ToString()
        };

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            handler.Handle(command, default));

        Assert.Equal("VAL_INVALID_RETURN_URL", exception.ErrorCode);
        Assert.DoesNotContain(context.DirectPaymentOrders, order => order.OrderId == orderId);
        Assert.DoesNotContain(context.WalletTransactions, transaction => transaction.ReferenceId == orderId);
    }

    [Fact]
    public async Task ProcessVnPayIpn_InvalidSignature_Returns97()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_InvalidSignature_Returns97));
        var vnPay = new FakeVnPayService { SignatureIsValid = false };
        var handler =
            new ProcessVnPayIpnCommandHandler(context, vnPay, NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

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
            ["vnp_CurrCode"] = "VND",
            ["vnp_TransactionNo"] = "123456"
        };

        var result = await handler.Handle(new ProcessVnPayIpnCommand(parameters, "127.0.0.1"), default);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal(TopupStatus.Success, topup.Status);
        Assert.Equal(50_000m, wallet.Balance);
        Assert.Single(context.WalletTransactions.Where(t =>
            t.Type == WalletTransactionType.TopupVnpay && t.WalletId == wallet.Id));
    }

    [Fact]
    public async Task ProcessVnPayIpn_Success_SettlesDirectPaymentWithoutChangingWalletBalance()
    {
        using var context = new TestWalletDbContext(
            nameof(ProcessVnPayIpn_Success_SettlesDirectPaymentWithoutChangingWalletBalance));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var directPayment = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            VnpTxnRef = "DIRECT-ORDER-1",
            Amount = 80_000m,
            Status = TopupStatus.Pending,
            RequestFingerprint = new string('A', 64)
        };
        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = directPayment.Amount,
            Currency = "Vnd",
            BalanceBefore = wallet.Balance,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Pending,
            ReferenceId = orderId
        };
        context.DirectPaymentOrders.Add(directPayment);
        context.WalletTransactions.Add(transaction);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = directPayment.VnpTxnRef,
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = "8000000",
            ["vnp_CurrCode"] = "VND",
            ["vnp_TransactionNo"] = "VN-123"
        };

        var result = await handler.Handle(new ProcessVnPayIpnCommand(parameters), default);
        var duplicate = await handler.Handle(new ProcessVnPayIpnCommand(parameters), default);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal("02", duplicate.ResponseCode);
        Assert.Equal(TopupStatus.Success, directPayment.Status);
        Assert.Equal("VN-123", directPayment.VnpTransactionNo);
        Assert.Equal(TransactionStatus.Success, transaction.Status);
        Assert.Equal(500_000m, wallet.Balance);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.DirectPaymentSuccess));
    }

    [Fact]
    public async Task ProcessVnPayIpn_DeclinedDirectPayment_MarksTransactionFailedWithoutChangingBalance()
    {
        using var context = new TestWalletDbContext(
            nameof(ProcessVnPayIpn_DeclinedDirectPayment_MarksTransactionFailedWithoutChangingBalance));
        var wallet = AddWallet(context, balance: 500_000m);
        var orderId = Guid.NewGuid();
        var directPayment = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            VnpTxnRef = "DIRECT-ORDER-2",
            Amount = 80_000m,
            Status = TopupStatus.Pending
        };
        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = directPayment.Amount,
            Currency = "Vnd",
            BalanceBefore = wallet.Balance,
            BalanceAfter = wallet.Balance,
            Status = TransactionStatus.Pending,
            ReferenceId = orderId
        };
        context.DirectPaymentOrders.Add(directPayment);
        context.WalletTransactions.Add(transaction);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);
        var result = await handler.Handle(
            new ProcessVnPayIpnCommand(new Dictionary<string, string>
            {
                ["vnp_TxnRef"] = directPayment.VnpTxnRef,
                ["vnp_ResponseCode"] = "24",
                ["vnp_Amount"] = "8000000",
                ["vnp_CurrCode"] = "VND"
            }),
            default);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal(TopupStatus.Failed, directPayment.Status);
        Assert.Equal(TransactionStatus.Failed, transaction.Status);
        Assert.Equal(500_000m, wallet.Balance);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.DirectPaymentFail));
    }

    [Fact]
    public async Task ProcessVnPayIpn_DuplicateSuccess_DoesNotCreditTwice()
    {
        using var context = new TestWalletDbContext(nameof(ProcessVnPayIpn_DuplicateSuccess_DoesNotCreditTwice));
        var wallet = AddWallet(context, balance: 0m);
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            VnpTxnRef = "TOPUP-DUPLICATE",
            Amount = 50_000m,
            Status = TopupStatus.Pending
        };
        context.WalletTopups.Add(topup);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);
        var command = new ProcessVnPayIpnCommand(new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "TOPUP-DUPLICATE",
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = "5000000",
            ["vnp_CurrCode"] = "VND"
        });

        var firstResult = await handler.Handle(command, default);
        var duplicateResult = await handler.Handle(command, default);

        Assert.Equal("00", firstResult.ResponseCode);
        Assert.Equal("02", duplicateResult.ResponseCode);
        Assert.Equal(TopupStatus.Success, topup.Status);
        Assert.Equal(50_000m, wallet.Balance);
        Assert.Single(context.WalletTransactions.Where(t =>
            t.Type == WalletTransactionType.TopupVnpay && t.ReferenceId == topup.Id));
    }

    [Theory]
    [InlineData("5000000.0", "VND")]
    [InlineData("5000000", "USD")]
    public async Task ProcessVnPayIpn_InvalidAmountOrCurrency_DoesNotProcessTopup(
        string amount,
        string currency)
    {
        using var context = new TestWalletDbContext(
            $"{nameof(ProcessVnPayIpn_InvalidAmountOrCurrency_DoesNotProcessTopup)}-{amount}-{currency}");
        var wallet = AddWallet(context, balance: 0m);
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            VnpTxnRef = "TOPUP-INVALID-INPUT",
            Amount = 50_000m,
            Status = TopupStatus.Pending
        };
        context.WalletTopups.Add(topup);
        context.SaveChanges();

        var handler = new ProcessVnPayIpnCommandHandler(
            context,
            new FakeVnPayService(),
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);
        var result = await handler.Handle(new ProcessVnPayIpnCommand(new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = "TOPUP-INVALID-INPUT",
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = amount,
            ["vnp_CurrCode"] = currency
        }), default);

        Assert.Equal("04", result.ResponseCode);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Equal(0m, wallet.Balance);
        Assert.DoesNotContain(context.WalletTransactions, transaction => transaction.ReferenceId == topup.Id);
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
            new ProcessWithdrawalCommand("APPROVE", ReferenceCode: "BANK-REF-001")
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
    public async Task ProcessWithdrawal_Reject_ReleasesLockedFunds()
    {
        using var context = new TestWalletDbContext(nameof(ProcessWithdrawal_Reject_ReleasesLockedFunds));
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
            new ProcessWithdrawalCommand("Reject")
            {
                WithdrawalId = withdrawal.Id,
                AdminUserId = Guid.NewGuid().ToString()
            },
            default);

        Assert.Equal("Rejected", result.Status);
        Assert.Equal(500_000m, wallet.Balance);
        Assert.Equal(0m, wallet.LockedBalance);
        Assert.DoesNotContain(context.WalletTransactions, t => t.ReferenceId == withdrawal.Id);
        Assert.Contains(context.AuditLogs, a =>
            a.Action == AuditAction.WithdrawalReject && a.EntityId == withdrawal.Id.ToString());
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
    public async Task RequestWithdrawal_ReservesFundsAndAuditsRequest()
    {
        using var context = new TestWalletDbContext(nameof(RequestWithdrawal_ReservesFundsAndAuditsRequest));
        var userId = Guid.NewGuid();
        var wallet = AddWallet(context, balance: 500_000m, lockedBalance: 50_000m, userId: userId);
        var handler = new RequestWithdrawalCommandHandler(context);
        var initialWithdrawalCount = context.WithdrawalRequests.Count();

        var result = await handler.Handle(
            new RequestWithdrawalCommand(
                Amount: 100_000m,
                BankCode: " VCB ",
                AccountNumber: " 1234567890 ",
                AccountHolderName: " Test User ")
            {
                UserId = userId.ToString()
            },
            default);

        var withdrawal = Assert.Single(context.WithdrawalRequests.Where(r => r.WalletId == wallet.Id));
        Assert.Equal(withdrawal.Id.ToString(), result.WithdrawalId);
        Assert.Equal(100_000m, result.Amount);
        Assert.Equal(0m, result.Fee);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(500_000m, wallet.Balance);
        Assert.Equal(150_000m, wallet.LockedBalance);
        Assert.Equal(350_000m, wallet.AvailableBalance);
        Assert.Equal("VCB", withdrawal.BankName);
        Assert.Equal("1234567890", withdrawal.BankAccountNumber);
        Assert.Equal("Test User", withdrawal.BankAccountHolder);
        Assert.Equal(initialWithdrawalCount + 1, context.WithdrawalRequests.Count());
        Assert.DoesNotContain(context.WalletTransactions, t => t.ReferenceId == withdrawal.Id);
        Assert.Single(context.AuditLogs.Where(a => a.Action == AuditAction.WithdrawalRequest));
    }

    [Fact]
    public async Task RequestWithdrawal_InsufficientAvailableBalance_DoesNotCreateRequest()
    {
        using var context =
            new TestWalletDbContext(nameof(RequestWithdrawal_InsufficientAvailableBalance_DoesNotCreateRequest));
        var userId = Guid.NewGuid();
        var wallet = AddWallet(context, balance: 100_000m, lockedBalance: 60_000m, userId: userId);
        var handler = new RequestWithdrawalCommandHandler(context);
        var initialWithdrawalCount = context.WithdrawalRequests.Count();
        var initialAuditCount = context.AuditLogs.Count();

        await Assert.ThrowsAsync<InsufficientBalanceException>(() => handler.Handle(
            new RequestWithdrawalCommand(
                Amount: 50_000m,
                BankCode: "VCB",
                AccountNumber: "1234567890",
                AccountHolderName: "Test User")
            {
                UserId = userId.ToString()
            },
            default));

        Assert.Equal(100_000m, wallet.Balance);
        Assert.Equal(60_000m, wallet.LockedBalance);
        Assert.Equal(initialWithdrawalCount, context.WithdrawalRequests.Count());
        Assert.Equal(initialAuditCount, context.AuditLogs.Count());
    }

    [Fact]
    public async Task RequestWithdrawal_MissingWallet_ReturnsNotFound()
    {
        using var context = new TestWalletDbContext(nameof(RequestWithdrawal_MissingWallet_ReturnsNotFound));
        var handler = new RequestWithdrawalCommandHandler(context);
        var initialWithdrawalCount = context.WithdrawalRequests.Count();
        var initialAuditCount = context.AuditLogs.Count();

        await Assert.ThrowsAsync<WalletNotFoundException>(() => handler.Handle(
            new RequestWithdrawalCommand(
                Amount: 100_000m,
                BankCode: "VCB",
                AccountNumber: "1234567890",
                AccountHolderName: "Test User")
            {
                UserId = Guid.NewGuid().ToString()
            },
            default));

        Assert.Equal(initialWithdrawalCount, context.WithdrawalRequests.Count());
        Assert.Equal(initialAuditCount, context.AuditLogs.Count());
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