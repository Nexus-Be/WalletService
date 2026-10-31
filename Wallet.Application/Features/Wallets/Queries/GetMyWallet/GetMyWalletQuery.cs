namespace Wallet.Application.Features.Wallets.Queries.GetMyWallet;

using MediatR;

public sealed record GetMyWalletQuery(string UserId) : IRequest<WalletResponse>;
