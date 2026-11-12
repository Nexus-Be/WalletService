namespace Wallet.Tests;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api.Controllers;
using Wallet.Application;
using Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Infrastructure.Payments;
using Wallet.Tests.Support;
using Xunit;

public class VnPayIpnContractTests
{
    private static WalletAccount AddWallet(TestWalletDbContext context, decimal balance = 0m)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = balance,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static WalletTopup AddTopup(TestWalletDbContext context, WalletAccount wallet, string txnRef, decimal amount = 50_000m)
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

    private static ProcessVnPayIpnCommandHandler Handler(TestWalletDbContext context, bool signatureValid = true) =>
        new(
            context,
            new FakeVnPayService { SignatureIsValid = signatureValid },
            NullLogger<ProcessVnPayIpnCommandHandler>.Instance);

    private static ProcessVnPayIpnCommand Ipn(
        string txnRef,
        string responseCode = "00",
        string amount = "5000000",
        string? transactionStatus = null,
        string? clientIp = "203.0.113.9")
    {
        var parameters = new Dictionary<string, string>
        {
            ["vnp_TxnRef"] = txnRef,
            ["vnp_ResponseCode"] = responseCode,
            ["vnp_Amount"] = amount,
            ["vnp_CurrCode"] = "VND",
            ["vnp_TransactionNo"] = "14000001"
        };
        if (transactionStatus is not null)
        {
            parameters["vnp_TransactionStatus"] = transactionStatus;
        }

        return new ProcessVnPayIpnCommand(parameters, clientIp);
    }

    // ---- Wire format ------------------------------------------------------------------

    [Fact]
    public void Response_SerializesAsRspCodeAndMessage()
    {
        var json = JsonSerializer.Serialize(new VnPayIpnResponse("00", "Confirm Success"));
        var webJson = JsonSerializer.Serialize(
            new VnPayIpnResponse("02", "Order already confirmed"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("{\"RspCode\":\"00\",\"Message\":\"Confirm Success\"}", json);
        Assert.Equal("{\"RspCode\":\"02\",\"Message\":\"Order already confirmed\"}", webJson);
        Assert.DoesNotContain("responseCode", json);
    }

    [Fact]
    public async Task Handler_UsesVnPayWordingForSuccessAndDuplicate()
    {
        using var context = new TestWalletDbContext(nameof(Handler_UsesVnPayWordingForSuccessAndDuplicate));
        AddTopup(context, AddWallet(context), "TOPUP-WORDING");

        var first = await Handler(context).Handle(Ipn("TOPUP-WORDING"), default);
        var second = await Handler(context).Handle(Ipn("TOPUP-WORDING"), default);

        Assert.Equal(("00", "Confirm Success"), (first.ResponseCode, first.Message));
        Assert.Equal(("02", "Order already confirmed"), (second.ResponseCode, second.Message));
    }

    // ---- vnp_TransactionStatus --------------------------------------------------------

    [Theory]
    [InlineData("00", "00", true)]
    [InlineData("00", null, true)]
    [InlineData("00", "02", false)]
    [InlineData("00", "01", false)]
    [InlineData("24", "00", false)]
    [InlineData("24", "02", false)]
    public async Task Topup_IsCreditedOnlyWhenResponseCodeAndTransactionStatusAreBothSuccessful(
        string responseCode,
        string? transactionStatus,
        bool credited)
    {
        using var context = new TestWalletDbContext(
            nameof(Topup_IsCreditedOnlyWhenResponseCodeAndTransactionStatusAreBothSuccessful) + responseCode + transactionStatus);
        var wallet = AddWallet(context);
        var topup = AddTopup(context, wallet, "TOPUP-STATUS");

        var result = await Handler(context).Handle(Ipn("TOPUP-STATUS", responseCode, transactionStatus: transactionStatus), default);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal(credited ? TopupStatus.Success : TopupStatus.Failed, topup.Status);
        Assert.Equal(credited ? 50_000m : 0m, wallet.Balance);
    }

    [Theory]
    [InlineData("00", "00", TransactionStatus.Success)]
    [InlineData("00", "02", TransactionStatus.Failed)]
    public async Task DirectPayment_FollowsTheSameSuccessRule(
        string responseCode,
        string transactionStatus,
        TransactionStatus expected)
    {
        using var context = new TestWalletDbContext(nameof(DirectPayment_FollowsTheSameSuccessRule) + transactionStatus);
        var wallet = AddWallet(context, 100_000m);
        var orderId = Guid.NewGuid();
        context.DirectPaymentOrders.Add(new DirectPaymentOrder
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            VnpTxnRef = "DIRECT-STATUS",
            Amount = 50_000m,
            Status = TopupStatus.Pending
        });
        context.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId.ToString(),
            Type = WalletTransactionType.DirectGatewayPayment,
            Amount = 50_000m,
            Status = TransactionStatus.Pending,
            ReferenceId = orderId
        });
        context.SaveChanges();

        var result = await Handler(context).Handle(Ipn("DIRECT-STATUS", responseCode, transactionStatus: transactionStatus), default);

        Assert.Equal("00", result.ResponseCode);
        Assert.Equal(expected, context.WalletTransactions.Single(t => t.ReferenceId == orderId).Status);
        Assert.Equal(100_000m, wallet.Balance);
    }

    // ---- Callback log -------------------------------------------------------------------

    [Fact]
    public async Task EveryOutcomeIsRecordedWithItsProcessingStatus()
    {
        using var context = new TestWalletDbContext(nameof(EveryOutcomeIsRecordedWithItsProcessingStatus));
        AddTopup(context, AddWallet(context), "TOPUP-LOG-OK");
        AddTopup(context, AddWallet(context), "TOPUP-LOG-AMT");

        await Handler(context).Handle(Ipn("TOPUP-LOG-OK"), default);
        await Handler(context).Handle(Ipn("TOPUP-LOG-OK"), default);
        await Handler(context).Handle(Ipn("TOPUP-LOG-AMT", amount: "1"), default);
        await Handler(context).Handle(Ipn("TOPUP-LOG-NONE"), default);
        await Handler(context, signatureValid: false).Handle(Ipn("TOPUP-LOG-SIG"), default);
        await Handler(context).Handle(Ipn(string.Empty), default);

        var statuses = context.VnpayIpnLogs.ToList().ToDictionary(
            log => (log.VnpTxnRef, log.ProcessingStatus),
            log => log);
        Assert.Equal(6, context.VnpayIpnLogs.Count());
        Assert.Contains(("TOPUP-LOG-OK", IpnProcessingStatus.Processed), statuses.Keys);
        Assert.Contains(("TOPUP-LOG-OK", IpnProcessingStatus.Duplicate), statuses.Keys);
        Assert.Contains(("TOPUP-LOG-AMT", IpnProcessingStatus.InvalidAmount), statuses.Keys);
        Assert.Contains(("TOPUP-LOG-NONE", IpnProcessingStatus.OrderNotFound), statuses.Keys);
        Assert.Contains(("TOPUP-LOG-SIG", IpnProcessingStatus.InvalidSignature), statuses.Keys);
        Assert.Contains((string.Empty, IpnProcessingStatus.SystemError), statuses.Keys);
    }

    [Fact]
    public async Task RecordedCallbackKeepsTheRawPayloadAndSource()
    {
        using var context = new TestWalletDbContext(nameof(RecordedCallbackKeepsTheRawPayloadAndSource));
        AddTopup(context, AddWallet(context), "TOPUP-LOG-RAW");

        await Handler(context).Handle(Ipn("TOPUP-LOG-RAW", clientIp: "198.51.100.7"), default);

        var log = Assert.Single(context.VnpayIpnLogs);
        Assert.Equal("00", log.VnpResponseCode);
        Assert.Equal("14000001", log.VnpTransactionNo);
        Assert.Equal("198.51.100.7", log.ClientIp);
        using var payload = JsonDocument.Parse(log.RawPayload!);
        Assert.Equal("TOPUP-LOG-RAW", payload.RootElement.GetProperty("vnp_TxnRef").GetString());
        Assert.Equal("5000000", payload.RootElement.GetProperty("vnp_Amount").GetString());
    }

    [Fact]
    public async Task RejectedCallbackIsStillRecorded_AndLeavesPaymentsUntouched()
    {
        using var context = new TestWalletDbContext(nameof(RejectedCallbackIsStillRecorded_AndLeavesPaymentsUntouched));
        var wallet = AddWallet(context);
        var topup = AddTopup(context, wallet, "TOPUP-LOG-FORGED");

        var result = await Handler(context, signatureValid: false).Handle(Ipn("TOPUP-LOG-FORGED"), default);

        Assert.Equal("97", result.ResponseCode);
        Assert.Equal(TopupStatus.Pending, topup.Status);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(IpnProcessingStatus.InvalidSignature, Assert.Single(context.VnpayIpnLogs).ProcessingStatus);
    }

    [Fact]
    public async Task RecordedCallbackIsBoundedInSize()
    {
        using var context = new TestWalletDbContext(nameof(RecordedCallbackIsBoundedInSize));
        var parameters = Enumerable.Range(0, 120).ToDictionary(i => $"vnp_Extra{i}", _ => new string('x', 5_000));
        parameters["vnp_TxnRef"] = new string('R', 400);
        parameters["vnp_ResponseCode"] = new string('9', 200);

        await Handler(context).Handle(new ProcessVnPayIpnCommand(parameters, new string('1', 200)), default);

        var log = Assert.Single(context.VnpayIpnLogs);
        Assert.Equal(100, log.VnpTxnRef.Length);
        Assert.Equal(50, log.VnpResponseCode!.Length);
        Assert.Equal(50, log.ClientIp!.Length);
        using var payload = JsonDocument.Parse(log.RawPayload!);
        Assert.True(payload.RootElement.EnumerateObject().Count() <= 50);
        Assert.All(payload.RootElement.EnumerateObject(), property => Assert.True(property.Value.GetString()!.Length <= 500));
    }

}
