namespace Wallet.Application.Features.Wallets.Commands.FreezeWallet;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record FreezeWalletCommand(
    [NonEmptyGuid] Guid UserId,
    [StringLength(500, ErrorMessage = "Reason must be 500 characters or fewer.")]
    string? Reason = null) : IRequest<WalletFreezeStatusResponse>, IRequestValidation
{
    [JsonIgnore] public string? ActorUserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (error is not null)
        {
            yield return error;
        }
    }
}
