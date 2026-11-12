namespace Wallet.Application.Features.Stripe.Commands.ProcessStripeWebhook;

using MediatR;

public sealed record ProcessStripeWebhookCommand(byte[] Payload, string Signature) : IRequest<Unit>;
