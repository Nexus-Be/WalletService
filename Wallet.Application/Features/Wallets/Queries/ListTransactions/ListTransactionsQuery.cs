namespace Wallet.Application.Features.Wallets.Queries.ListTransactions;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Validation;
using global::Wallet.Application.Common.Models;
using global::Wallet.Domain.Enums;

public sealed record ListTransactionsQuery(
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 10,
    string? Type = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    string? Status = null,
    Guid? ReferenceId = null) : IRequest<PagedData<WalletTransactionResponse>>, IRequestValidation
{
    [JsonIgnore] public string UserId { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var userIdError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userIdError is not null)
        {
            yield return userIdError;
        }

        if (!string.IsNullOrWhiteSpace(Type) &&
            !new[] { "ALL", "DEPOSIT", "WITHDRAWAL", "PAYMENT", "REFUND", "TRANSFER", "ADJUSTMENT", "AUCTION" }
                .Contains(Type.Trim(), StringComparer.OrdinalIgnoreCase) &&
            !Enum.GetNames<WalletTransactionType>()
                .Contains(Type.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "Transaction type is invalid.",
                new[] { nameof(Type) });
        }

        if (!string.IsNullOrWhiteSpace(Status) && !Status.TryToEnum<TransactionStatus>(out _))
        {
            yield return new ValidationResult(
                "Transaction status is invalid.",
                new[] { nameof(Status) });
        }

        if (ReferenceId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Reference ID must be a valid UUID.",
                new[] { nameof(ReferenceId) });
        }

        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value > ToDate.Value)
        {
            yield return new ValidationResult(
                "From date must be earlier than or equal to to date.",
                new[] { nameof(FromDate), nameof(ToDate) });
        }
    }
}

public sealed record WalletTransactionResponse(
    string Id,
    string WalletId,
    string UserId,
    string Type,
    decimal Amount,
    string Currency,
    decimal BalanceAfter,
    string Status,
    string Note,
    DateTimeOffset CreatedAt);