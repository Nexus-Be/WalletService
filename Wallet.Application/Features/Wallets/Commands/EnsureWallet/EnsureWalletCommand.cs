namespace Wallet.Application.Features.Wallets.Commands.EnsureWallet;

using System;
using MediatR;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;

public sealed record EnsureWalletCommand(Guid UserId) : IRequest<WalletResponse>;
