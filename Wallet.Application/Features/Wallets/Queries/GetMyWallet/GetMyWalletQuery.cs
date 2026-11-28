namespace Wallet.Application.Features.Wallets.Queries.GetMyWallet;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record GetMyWalletQuery(
    [Required(ErrorMessage = "User ID is required.")]
    [NonEmptyGuid(ErrorMessage = "User ID must be a valid UUID.")]
    string UserId) : IRequest<WalletResponse>
{
    [JsonIgnore] public bool AutoCreate { get; init; } = true;
}