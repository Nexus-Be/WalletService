namespace Wallet.Tests.Support;

using Wallet.Application.Interfaces;

public sealed class FakeStripeCheckoutClient : IStripeCheckoutClient
{
    public int Calls { get; private set; }

    public Task<StripeCheckoutSession> CreateSessionAsync(StripeCheckoutRequest request, CancellationToken token)
    {
        Calls++;
        return Task.FromResult(new StripeCheckoutSession($"cs_test_{request.AttemptId:N}",
            "https://checkout.stripe.com/test", DateTimeOffset.UtcNow.AddHours(1)));
    }

    public Task<StripeSessionSnapshot> GetSessionAsync(string id, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<StripePaymentIntentSnapshot> GetPaymentIntentAsync(string id, CancellationToken token) =>
        throw new NotSupportedException();
}
