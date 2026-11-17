namespace Wallet.Application.Features.Stripe.Commands.ReconcilePendingTopups;

using MediatR;

public sealed record ReconcilePendingTopupsCommand : IRequest<int>;
