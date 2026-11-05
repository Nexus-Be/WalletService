namespace Wallet.Application.Features.Transfers.Commands.Transfer;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record TransferCommand(
    [Required(ErrorMessage = "Recipient user ID is required.")]
    [NonEmptyGuid(ErrorMessage = "Recipient user ID must be a valid UUID.")]
    string RecipientUserId,
    [Range(1000, 100000000, ErrorMessage = "Transfer amount must be between 1,000 and 100,000,000 VND.")]
    [MoneyPrecision]
    decimal Amount,
    string? Note = null) : IRequest<TransferResponse>, IRequestValidation
{
    [JsonIgnore] public string SenderUserId { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var senderUserIdError =
            RequestValidation.ValidateGuid(SenderUserId, nameof(SenderUserId), "Sender user ID");
        if (senderUserIdError is not null)
        {
            yield return senderUserIdError;
        }
    }
}

public sealed record TransferResponse(
    string TransactionId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);