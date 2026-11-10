namespace Wallet.Application.Features.AdminWallets.Queries.ListWalletLedger;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using MediatR;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Domain.Enums;

public sealed record ListWalletLedgerQuery(
    [property: Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [property: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 10,
    Guid? WalletId = null,
    string? Type = null,
    string? Status = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<PagedData<AdminWalletLedgerEntry>>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (WalletId == Guid.Empty)
        {
            yield return new ValidationResult("Wallet ID must be a non-empty UUID.", new[] { nameof(WalletId) });
        }

        if (!string.IsNullOrWhiteSpace(Type) && !IsDefinedName<WalletTransactionType>(Type))
        {
            yield return new ValidationResult("Transaction type is invalid.", new[] { nameof(Type) });
        }

        if (!string.IsNullOrWhiteSpace(Status) && !IsDefinedName<TransactionStatus>(Status))
        {
            yield return new ValidationResult("Transaction status is invalid.", new[] { nameof(Status) });
        }

        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value > ToDate.Value)
        {
            yield return new ValidationResult(
                "From date must be earlier than or equal to to date.",
                new[] { nameof(FromDate), nameof(ToDate) });
        }
    }

    private static bool IsDefinedName<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}

public sealed record AdminWalletLedgerEntry(
    string TransactionId,
    string WalletId,
    string Type,
    decimal Amount,
    string Currency,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string Status,
    string? ReferenceId,
    DateTimeOffset CreatedAt);
