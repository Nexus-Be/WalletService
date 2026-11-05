namespace Wallet.Application.Features.Wallets.Queries.GetMyWallet;

using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record GetMyWalletQuery(
    [Required(ErrorMessage = "User ID is required.")]
    [NonEmptyGuid(ErrorMessage = "User ID must be a valid UUID.")]
    string UserId) : IRequest<WalletResponse>;