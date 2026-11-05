namespace Wallet.Application.Features.Wallets.Queries.ListTransactions;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using MediatR;
using Wallet.Application.Common.Validation;
using global::Wallet.Application.Common.Models;
using global::Wallet.Domain.Enums;

public sealed record ListTransactionsQuery(
    [property: Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [property: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 10,
    string? Type = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<PagedData<WalletTransactionResponse>>, IRequestValidation
{
    public string UserId { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var userIdError = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (userIdError is not null)
        {
            yield return userIdError;
        }

        if (!string.IsNullOrWhiteSpace(Type) &&
            !new[] { "ALL", "DEPOSIT", "WITHDRAWAL", "PAYMENT", "REFUND" }
                .Contains(Type.Trim(), StringComparer.OrdinalIgnoreCase) &&
            !Enum.GetNames<WalletTransactionType>()
                .Contains(Type.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "Transaction type is invalid.",
                new[] { nameof(Type) });
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