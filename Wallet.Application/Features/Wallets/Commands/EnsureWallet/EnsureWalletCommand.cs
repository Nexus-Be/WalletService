namespace Wallet.Application.Features.Wallets.Commands.EnsureWallet;

using System;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;

public sealed record EnsureWalletCommand(
    [NonEmptyGuid(ErrorMessage = "User ID must be a valid UUID.")]
    Guid UserId) : IRequest<WalletResponse>;