namespace Wallet.Tests;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api.Controllers;
using Wallet.Application.Features.Topups.Queries.GetTopupStatus;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class GetTopupStatusEndpointTests
{
    [Fact]
    public async Task Controller_SendsAuthenticatedOwnerAndTopupId()
    {
        var sender = new CapturingSender();
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["x-user-id"] = owner.ToString();
        var controller = new WalletsController(sender, NullLogger<WalletsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var result = await controller.GetTopupStatus(id, default);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetTopupStatusQuery>(sender.Captured);
        Assert.Equal(id, query.TopupId);
        Assert.Equal(owner, query.UserId);
    }

    [Fact]
    public async Task Controller_WithoutIdentityDoesNotSendQuery()
    {
        var sender = new CapturingSender();
        var controller = new WalletsController(sender, NullLogger<WalletsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.GetTopupStatus(Guid.NewGuid(), default);

        Assert.IsNotType<OkObjectResult>(result);
        Assert.Null(sender.Captured);
    }

    [Fact]
    public async Task Handler_ReturnsOwnerStatusAndHidesOtherOwners()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = owner, Currency = CurrencyCode.Vnd };
        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = 100000, Status = TopupStatus.Success
        };
        context.Wallets.Add(wallet);
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = owner, Purpose = StripePaymentPurpose.Topup, WalletTopupId = topup.Id,
            Amount = topup.Amount, RequestFingerprint = "fingerprint", ProviderIdempotencyKey = "provider-key"
        });
        await context.SaveChangesAsync();
        var handler = new GetTopupStatusQueryHandler(context);

        var result = await handler.Handle(new GetTopupStatusQuery(topup.Id) { UserId = owner }, default);
        var error = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetTopupStatusQuery(topup.Id) { UserId = Guid.NewGuid() }, default));

        Assert.Equal(topup.Id, result.TransactionId);
        Assert.Equal("Success", result.Status);
        Assert.Equal("PAYMENT_NOT_FOUND", error.ErrorCode);
    }

    private sealed class CapturingSender : ISender
    {
        public object? Captured { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Captured = request;
            object response = new TopupStatusResponse(Guid.NewGuid(), 100000, "Vnd", "Pending",
                DateTimeOffset.UtcNow, null);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
