namespace Wallet.Api.Common;

using Wallet.Domain.Exceptions;
using Wallet.Infrastructure.Payments;

public static class StripeWebhookBodyReader
{
    public static async Task<byte[]> ReadAsync(Stream body, long? contentLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        const int limit = StripeWebhookVerifier.MaximumPayloadBytes;
        if (contentLength > limit)
            throw TooLarge();

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var remaining = limit + 1 - (int)buffer.Length;
            var count = await body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken);
            if (count == 0)
                return buffer.ToArray();
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            if (buffer.Length > limit)
                throw TooLarge();
        }
    }

    private static BusinessRuleValidationException TooLarge() =>
        new("WEBHOOK_PAYLOAD_TOO_LARGE", "Stripe webhook payload is too large.", 413);
}
