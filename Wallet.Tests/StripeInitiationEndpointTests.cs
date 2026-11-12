namespace Wallet.Tests;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Api.Controllers;
using Wallet.Application;
using Wallet.Application.Common.Exceptions;
using Wallet.Application.Features.Checkout.Commands.CheckoutDirect;
using Wallet.Application.Features.Topups.Commands.TopupStripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class StripeInitiationEndpointTests
{
    [Fact]
    public async Task Topup_UsesAuthenticatedOwnerAndReplaysSameSession()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = await AddWallet(db);
        var provider = new FakeStripeCheckoutClient();
        await using var services = Services(db, provider);
        var controller = Controller(services, owner);
        var first = Assert.IsType<ObjectResult>(await controller.TopupStripe(new(100000), "key-1", default));
        var second = Assert.IsType<ObjectResult>(await controller.TopupStripe(new(100000), "key-1", default));
        var result = Assert.IsType<TopupStripeResponse>(first.Value);
        Assert.Equal(201, first.StatusCode);
        Assert.Equal(result, Assert.IsType<TopupStripeResponse>(second.Value));
        Assert.Equal(owner, db.StripePaymentAttempts.Single().OwnerId);
        Assert.Equal(1, provider.Calls);
        var error = await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => controller.TopupStripe(new(200000), "key-1", default));
        Assert.Equal("PAYMENT_CONFLICT", error.ErrorCode);
    }

    [Theory]
    [InlineData(9999, "key")]
    [InlineData(10000.5, "key")]
    [InlineData(100000000, "key")]
    [InlineData(100000, "")]
    [InlineData(100000, "invalid key")]
    public async Task Topup_InvalidInput_DoesNotCreateAttempt(decimal amount, string key)
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = await AddWallet(db);
        var provider = new FakeStripeCheckoutClient();
        await using var services = Services(db, provider);
        await Assert.ThrowsAsync<RequestValidationException>(
            () => Controller(services, owner).TopupStripe(new(amount), key, default));
        Assert.Empty(db.StripePaymentAttempts);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Direct_ReplaysSessionWithoutChangingWalletBalance()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var owner = await AddWallet(db);
        var provider = new FakeStripeCheckoutClient();
        await using var services = Services(db, provider);
        var controller = Controller(services, owner);
        var command = new CheckoutDirectCommand(Guid.NewGuid().ToString(), 100000);
        var first = Assert.IsType<OkObjectResult>(await controller.CheckoutDirect(command, default));
        var second = Assert.IsType<OkObjectResult>(await controller.CheckoutDirect(command, default));
        Assert.Equal(Assert.IsType<DirectPaymentResponse>(first.Value), second.Value);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(300000m, db.Wallets.Single().Balance);
        Assert.Equal(owner, db.StripePaymentAttempts.Single().OwnerId);
    }

    [Fact]
    public async Task MissingIdentity_DoesNotInitiateEitherPayment()
    {
        using var db = new TestWalletDbContext(Guid.NewGuid().ToString());
        var provider = new FakeStripeCheckoutClient();
        await using var services = Services(db, provider);
        var controller = Controller(services, null);
        Assert.Equal(401, Assert.IsType<ObjectResult>(
            await controller.TopupStripe(new(100000), "key", default)).StatusCode);
        Assert.Equal(401, Assert.IsType<ObjectResult>(
            await controller.CheckoutDirect(new(Guid.NewGuid().ToString(), 100000), default)).StatusCode);
        Assert.Empty(db.StripePaymentAttempts);
        Assert.Equal(0, provider.Calls);
    }

    private static async Task<Guid> AddWallet(TestWalletDbContext db)
    {
        var owner = Guid.NewGuid();
        db.Wallets.Add(new WalletAccount
        {
            Id = Guid.NewGuid(), UserId = owner, Balance = 300000, Currency = CurrencyCode.Vnd, Status = "ACTIVE"
        });
        await db.SaveChangesAsync();
        return owner;
    }

    private static ServiceProvider Services(TestWalletDbContext db, FakeStripeCheckoutClient provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddSingleton<IWalletDbContext>(db);
        services.AddSingleton<IStripeCheckoutClient>(provider);
        return services.BuildServiceProvider();
    }

    private static WalletsController Controller(ServiceProvider services, Guid? owner)
    {
        var context = new DefaultHttpContext();
        if (owner.HasValue)
            context.Request.Headers["x-user-id"] = owner.Value.ToString();
        return new WalletsController(services.GetRequiredService<ISender>(), NullLogger<WalletsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
