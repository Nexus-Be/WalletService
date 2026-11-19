namespace Wallet.Application.Features.Wallets.Queries.GetTransactionById;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record GetTransactionByIdQuery(
    [NonEmptyGuid] Guid TransactionId) : IRequest<WalletTransactionDetailResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var txError = RequestValidation.ValidateGuid(TransactionId, nameof(TransactionId), "Transaction ID");
        if (txError is not null)
        {
            yield return txError;
        }

        var userError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userError is not null)
        {
            yield return userError;
        }
    }
}

public sealed record WalletTransactionDetailResponse(
    string TransactionId,
    string WalletId,
    string UserId,
    string Type,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string Currency,
    string Status,
    Guid? ReferenceId,
    string? Note,
    string? GatewayStatus,
    string? GatewayReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
