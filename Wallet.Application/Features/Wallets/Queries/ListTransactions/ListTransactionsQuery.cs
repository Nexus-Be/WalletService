namespace Wallet.Application.Features.Wallets.Queries.ListTransactions;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using global::Wallet.Application.Common.Models;

public sealed record ListTransactionsQuery(
    [property: Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [property: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 10,
    string? Type = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<PagedData<WalletTransactionResponse>>
{
    public string UserId { get; init; } = string.Empty;
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
