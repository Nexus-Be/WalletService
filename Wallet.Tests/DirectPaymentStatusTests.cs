namespace Wallet.Tests;

using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api.Controllers;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Checkout.Queries.GetDirectPaymentStatus;
using Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;
using CheckoutDirectCommand = Wallet.Tests.Support.LegacyCheckout.CheckoutDirectCommand;
using CheckoutDirectCommandHandler = Wallet.Tests.Support.LegacyCheckout.CheckoutDirectCommandHandler;

public class DirectPaymentStatusTests
{
    private const string ReturnUrl = "https://return.test/done";

    private static WalletAccount AddWallet(TestWalletDbContext context)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Balance = 500_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        };
        context.Wallets.Add(wallet);
        context.SaveChanges();
        return wallet;
    }

    private static async Task<(WalletAccount Wallet, Guid OrderId, DirectPaymentOrder Order)> CheckoutAsync(
        TestWalletDbContext context,
        decimal amount = 80_000m)
    {
        var wallet = AddWallet(context);
        var orderId = Guid.NewGuid();
        await new CheckoutDirectCommandHandler(context, new FakeVnPayService()).Handle(
            new CheckoutDirectCommand(orderId.ToString(), amount, null, ReturnUrl) { UserId = wallet.UserId.ToString() },
            default);
        return (wallet, orderId, context.DirectPaymentOrders.Single(o => o.OrderId == orderId));
    }

    private static Task<VnPayIpnResponse> SettleAsync(
        TestWalletDbContext context,
        DirectPaymentOrder order,
        string responseCode,
        string transactionNo = "14000001") =>
        new ProcessVnPayIpnCommandHandler(
                context,
                new FakeVnPayService(),
                NullLogger<ProcessVnPayIpnCommandHandler>.Instance)
            .Handle(
                new ProcessVnPayIpnCommand(new Dictionary<string, string>
                {
                    ["vnp_TxnRef"] = Assert.IsType<string>(order.VnpTxnRef),
                    ["vnp_ResponseCode"] = responseCode,
                    ["vnp_Amount"] = (order.Amount * 100).ToString("0"),
                    ["vnp_CurrCode"] = "VND",
                    ["vnp_TransactionNo"] = transactionNo
                }),
                default);

    private static GetDirectPaymentStatusQueryHandler Handler(TestWalletDbContext context) => new(context);

    private static GetDirectPaymentStatusQuery Query(Guid orderId, WalletAccount wallet) =>
        new(orderId.ToString()) { UserId = wallet.UserId.ToString() };

    [Fact]
    public async Task Status_IsPendingRightAfterCheckout()
    {
        using var context = new TestWalletDbContext(nameof(Status_IsPendingRightAfterCheckout));
        var (wallet, orderId, _) = await CheckoutAsync(context, 80_000m);

        var result = await Handler(context).Handle(Query(orderId, wallet), default);

        Assert.Equal("Pending", result.Status);
        Assert.Equal(80_000m, result.Amount);
        Assert.Equal("Vnd", result.Currency);
        Assert.Equal(orderId.ToString(), result.OrderId);
        Assert.Null(result.GatewayTransactionNo);
    }

    [Fact]
    public async Task Status_BecomesSuccessWithGatewayTransactionNoAfterIpn()
    {
        using var context = new TestWalletDbContext(nameof(Status_BecomesSuccessWithGatewayTransactionNoAfterIpn));
        var (wallet, orderId, order) = await CheckoutAsync(context);

        var ipn = await SettleAsync(context, order, "00", "777");
        var result = await Handler(context).Handle(Query(orderId, wallet), default);

        Assert.Equal("00", ipn.ResponseCode);
        Assert.Equal("Success", result.Status);
        Assert.Equal("777", result.GatewayTransactionNo);
        Assert.Equal(500_000m, wallet.Balance);
    }

    [Fact]
    public async Task StripeStatus_UsesSuccessfulAttemptPaymentIntent()
    {
        using var context = new TestWalletDbContext(nameof(StripeStatus_UsesSuccessfulAttemptPaymentIntent));
        var wallet = AddWallet(context);
        var orderId = Guid.NewGuid();
        var order = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(), OrderId = orderId, Amount = 80000, Status = TopupStatus.Success
        };
        context.DirectPaymentOrders.Add(order);
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = wallet.UserId.ToString(),
            ReferenceId = orderId, Type = WalletTransactionType.DirectGatewayPayment,
            Amount = 80000, Currency = "Vnd", Status = TransactionStatus.Success
        });
        context.StripePaymentAttempts.Add(new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = wallet.UserId, Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = order.Id, Amount = 80000, RequestFingerprint = "fingerprint",
            ProviderIdempotencyKey = "provider-key", Status = StripePaymentAttemptStatus.Successful,
            PaymentIntentId = "pi_success"
        });
        await context.SaveChangesAsync();

        var result = await Handler(context).Handle(Query(orderId, wallet), default);

        Assert.Equal("Success", result.Status);
        Assert.Equal("pi_success", result.GatewayTransactionNo);
    }

    [Fact]
    public async Task StripeStatus_PendingReplacementDoesNotReturnPreviousIntent()
    {
        using var context = new TestWalletDbContext(nameof(StripeStatus_PendingReplacementDoesNotReturnPreviousIntent));
        var wallet = AddWallet(context);
        var orderId = Guid.NewGuid();
        var order = new DirectPaymentOrder
        {
            Id = Guid.NewGuid(), OrderId = orderId, Amount = 80000, Status = TopupStatus.Pending
        };
        context.DirectPaymentOrders.Add(order);
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = wallet.UserId.ToString(),
            ReferenceId = orderId, Type = WalletTransactionType.DirectGatewayPayment,
            Amount = 80000, Currency = "Vnd", Status = TransactionStatus.Pending
        });
        context.StripePaymentAttempts.AddRange(
            new StripePaymentAttempt
            {
                Id = Guid.NewGuid(), OwnerId = wallet.UserId, Purpose = StripePaymentPurpose.DirectOrder,
                DirectPaymentOrderId = order.Id, Amount = 80000, RequestFingerprint = "fingerprint",
                ProviderIdempotencyKey = "previous-key", Status = StripePaymentAttemptStatus.Failed,
                BlocksReplacement = false, PaymentIntentId = "pi_old", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new StripePaymentAttempt
            {
                Id = Guid.NewGuid(), OwnerId = wallet.UserId, Purpose = StripePaymentPurpose.DirectOrder,
                DirectPaymentOrderId = order.Id, Amount = 80000, RequestFingerprint = "fingerprint",
                ProviderIdempotencyKey = "current-key", Status = StripePaymentAttemptStatus.CreationPending,
                BlocksReplacement = true, CreatedAt = DateTimeOffset.UtcNow
            });
        await context.SaveChangesAsync();

        var result = await Handler(context).Handle(Query(orderId, wallet), default);

        Assert.Equal("Pending", result.Status);
        Assert.Null(result.GatewayTransactionNo);
    }

    [Fact]
    public async Task Status_BecomesFailedAfterGatewayFailure()
    {
        using var context = new TestWalletDbContext(nameof(Status_BecomesFailedAfterGatewayFailure));
        var (wallet, orderId, order) = await CheckoutAsync(context);

        await SettleAsync(context, order, "24");
        var result = await Handler(context).Handle(Query(orderId, wallet), default);

        Assert.Equal("Failed", result.Status);
    }

    [Fact]
    public async Task Status_OtherUserGetsNotFound_ButPrivilegedCallerCanRead()
    {
        using var context = new TestWalletDbContext(nameof(Status_OtherUserGetsNotFound_ButPrivilegedCallerCanRead));
        var (_, orderId, _) = await CheckoutAsync(context);
        var stranger = AddWallet(context);

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => Handler(context).Handle(Query(orderId, stranger), default));
        var privileged = await Handler(context).Handle(Query(orderId, stranger) with { AllowAnyUser = true }, default);

        Assert.Equal("PAYMENT_NOT_FOUND", exception.ErrorCode);
        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("Pending", privileged.Status);
    }

    [Fact]
    public async Task Status_UnknownOrderGetsNotFound()
    {
        using var context = new TestWalletDbContext(nameof(Status_UnknownOrderGetsNotFound));
        var wallet = AddWallet(context);

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => Handler(context).Handle(Query(Guid.NewGuid(), wallet), default));

        Assert.Equal("PAYMENT_NOT_FOUND", exception.ErrorCode);
    }

    [Theory]
    [InlineData("not-a-guid", "11111111-1111-1111-1111-111111111111", "VAL_INVALID_ORDER_ID")]
    [InlineData("11111111-1111-1111-1111-111111111111", "", "VAL_INVALID_USER_ID")]
    [InlineData("11111111-1111-1111-1111-111111111111", "bad", "VAL_INVALID_USER_ID")]
    public async Task Handler_RejectsInvalidIdentifiers(string orderId, string userId, string expectedCode)
    {
        using var context = new TestWalletDbContext(nameof(Handler_RejectsInvalidIdentifiers) + orderId + userId);

        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => Handler(context).Handle(
            new GetDirectPaymentStatusQuery(orderId) { UserId = userId }, default));

        Assert.Equal(expectedCode, exception.ErrorCode);
    }

    [Theory]
    [InlineData("", "11111111-1111-1111-1111-111111111111", false)]
    [InlineData("not-a-guid", "11111111-1111-1111-1111-111111111111", false)]
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111", false)]
    [InlineData("11111111-1111-1111-1111-111111111111", "", false)]
    [InlineData("11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", true)]
    public void Validation_RequiresOrderAndCallerIds(string orderId, string userId, bool valid)
    {
        var isValid = RequestValidation.TryValidate(
            new GetDirectPaymentStatusQuery(orderId) { UserId = userId },
            new List<ValidationResult>());

        Assert.Equal(valid, isValid);
    }

    [Fact]
    public void Validation_PrivilegedCallerNeedsNoUserId()
    {
        var isValid = RequestValidation.TryValidate(
            new GetDirectPaymentStatusQuery("11111111-1111-1111-1111-111111111111") { AllowAnyUser = true },
            new List<ValidationResult>());

        Assert.True(isValid);
    }

    // ---- Controller -------------------------------------------------------------------

    private sealed class CapturingSender : ISender
    {
        public object? Captured { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Captured = request;
            object response = new DirectPaymentStatusResponse(
                "tx", "order", 1m, "Vnd", "Pending", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static WalletsController NewController(CapturingSender sender, params (string Name, string Value)[] headers)
    {
        var httpContext = new DefaultHttpContext();
        foreach (var (name, value) in headers)
        {
            httpContext.Request.Headers[name] = value;
        }

        return new WalletsController(sender, NullLogger<WalletsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task Controller_UsesCallerIdentityFromHeaders()
    {
        var sender = new CapturingSender();
        var userId = Guid.NewGuid().ToString();
        var controller = NewController(sender, ("x-user-id", userId), ("x-user-roles", "buyer"));

        var result = await controller.GetDirectPaymentStatus(Guid.NewGuid().ToString(), default);

        Assert.IsType<OkObjectResult>(result);
        var captured = Assert.IsType<GetDirectPaymentStatusQuery>(sender.Captured);
        Assert.Equal(userId, captured.UserId);
        Assert.False(captured.AllowAnyUser);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("staff")]
    public async Task Controller_AdminAndStaffMayReadAnyPayment(string role)
    {
        var sender = new CapturingSender();
        var controller = NewController(sender, ("x-user-id", Guid.NewGuid().ToString()), ("x-user-roles", role));

        await controller.GetDirectPaymentStatus(Guid.NewGuid().ToString(), default);

        Assert.True(Assert.IsType<GetDirectPaymentStatusQuery>(sender.Captured).AllowAnyUser);
    }

    [Fact]
    public async Task Controller_WithoutIdentityIsRejectedAndNeverQueries()
    {
        var sender = new CapturingSender();
        var controller = NewController(sender);

        var result = await controller.GetDirectPaymentStatus(Guid.NewGuid().ToString(), default);

        Assert.IsNotType<OkObjectResult>(result);
        Assert.Null(sender.Captured);
    }
}
