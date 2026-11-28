namespace Wallet.Tests;

using System.Text;
using Wallet.Api.Common;
using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Payments;
using Xunit;

public sealed class StripeWebhookBodyReaderTests
{
    [Fact]
    public async Task Body_IsReturnedWithoutChangingBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"message\":\"test\\r\\nvalue\"}");
        using var stream = new MemoryStream(bytes);
        Assert.Equal(bytes, await StripeWebhookBodyReader.ReadAsync(stream, bytes.Length, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MaximumSize_IsAcceptedWithOrWithoutContentLength(bool knownLength)
    {
        var bytes = new byte[StripeWebhookVerifier.MaximumPayloadBytes];
        using var stream = new MemoryStream(bytes);
        var result = await StripeWebhookBodyReader.ReadAsync(stream, knownLength ? bytes.Length : null, default);
        Assert.Equal(bytes.Length, result.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OversizeBody_IsRejectedWithOversizeMissingOrMisleadingContentLength(int lengthMode)
    {
        var bytes = new byte[StripeWebhookVerifier.MaximumPayloadBytes + 1];
        using var stream = new MemoryStream(bytes);
        long? length = lengthMode switch { 0 => bytes.Length, 1 => null, _ => 1 };
        var exception = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            StripeWebhookBodyReader.ReadAsync(stream, length, default));
        Assert.Equal(413, exception.StatusCode);
        Assert.Equal("WEBHOOK_PAYLOAD_TOO_LARGE", exception.ErrorCode);
        if (lengthMode == 0)
            Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var stream = new MemoryStream(new byte[1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StripeWebhookBodyReader.ReadAsync(stream, null, cancellation.Token));
    }
}
