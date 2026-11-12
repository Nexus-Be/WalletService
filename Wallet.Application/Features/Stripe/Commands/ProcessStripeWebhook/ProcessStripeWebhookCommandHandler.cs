namespace Wallet.Application.Features.Stripe.Commands.ProcessStripeWebhook;

using MediatR;
using Microsoft.Extensions.Logging;
using Wallet.Application.Interfaces;

internal sealed class ProcessStripeWebhookCommandHandler(
    IWalletDbContext context,
    IStripeWebhookVerifier verifier,
    IStripeCheckoutClient provider,
    ILogger<ProcessStripeWebhookCommandHandler> logger)
    : IRequestHandler<ProcessStripeWebhookCommand, Unit>
{
    public async Task<Unit> Handle(ProcessStripeWebhookCommand request, CancellationToken cancellationToken)
    {
        var outcome = await StripeWebhookProcessing.ProcessAsync(
            context, verifier, provider, request.Payload, request.Signature, cancellationToken);
        if (outcome == StripeWebhookProcessingOutcome.RetryRequired)
            throw new InvalidOperationException("Stripe webhook processing requires a retry.");
        if (outcome == StripeWebhookProcessingOutcome.ReconciliationRequired)
            logger.LogCritical("Stripe webhook requires reconciliation. Inspect persisted webhook events and payment attempts.");
        return Unit.Value;
    }
}
