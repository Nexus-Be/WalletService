namespace Wallet.Application.Features.Wallets.Queries.ListTransactions;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common;
using global::Wallet.Application.Common.Models;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Enums;

internal sealed class ListTransactionsQueryHandler : IRequestHandler<ListTransactionsQuery, PagedData<WalletTransactionResponse>>
{
    private readonly IWalletDbContext _context;

    public ListTransactionsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<PagedData<WalletTransactionResponse>> Handle(ListTransactionsQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userGuid) || userGuid == Guid.Empty)
            throw new ArgumentException("User ID must be a valid UUID.", nameof(request));

        var userId = request.UserId;
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);

        var query = _context.WalletTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var types = request.Type.Trim().ToUpperInvariant() switch
            {
                "ALL" => Array.Empty<WalletTransactionType>(),
                "DEPOSIT" => new[] { WalletTransactionType.TopupVnpay },
                "WITHDRAWAL" => new[]
                {
                    WalletTransactionType.WithdrawalDebit,
                    WalletTransactionType.WithdrawalReversal
                },
                "PAYMENT" => new[]
                {
                    WalletTransactionType.PurchaseDebit,
                    WalletTransactionType.DirectGatewayPayment
                },
                "REFUND" => new[] { WalletTransactionType.RefundCredit },
                _ when Enum.TryParse<WalletTransactionType>(request.Type.Trim(), true, out var txType) =>
                    new[] { txType },
                _ => throw new ArgumentException("Transaction type is invalid.", nameof(request))
            };

            if (types.Length > 0)
                query = query.Where(t => types.Contains(t.Type));
        }

        if (request.FromDate.HasValue)
            query = query.Where(t => t.CreatedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(t => t.CreatedAt <= request.ToDate.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WalletTransactionResponse(
                t.Id.ToString(),
                t.WalletId.ToString(),
                t.UserId ?? string.Empty,
                t.Type.ToString(),
                t.Amount,
                t.Currency ?? "VND",
                t.BalanceAfter,
                t.Status.ToString(),
                t.Note ?? string.Empty,
                t.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PagedData<WalletTransactionResponse>(items, PaginationMetadata.Create(page, pageSize, total));
    }
}
