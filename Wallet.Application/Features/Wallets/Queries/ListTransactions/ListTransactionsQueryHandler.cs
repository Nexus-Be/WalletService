namespace Wallet.Application.Features.Wallets.Queries.ListTransactions;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common;
using global::Wallet.Application.Common.Extensions;
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
                "DEPOSIT" => new[] { WalletTransactionType.TopupVnpay, WalletTransactionType.TopupStripe },
                "WITHDRAWAL" => new[]
                {
                    WalletTransactionType.WithdrawalDebit,
                    WalletTransactionType.WithdrawalReversal
                },
                "PAYMENT" => new[]
                {
                    WalletTransactionType.PurchaseDebit,
                    WalletTransactionType.DirectGatewayPayment,
                    WalletTransactionType.OrderPaymentAdjustmentDebit,
                    WalletTransactionType.AuctionHoldCapture
                },
                "REFUND" => new[]
                {
                    WalletTransactionType.RefundCredit,
                    WalletTransactionType.AuctionDepositRefund,
                    WalletTransactionType.EscrowCancellationRefund,
                    WalletTransactionType.OrderPaymentAdjustmentRefund
                },
                "TRANSFER" => new[]
                {
                    WalletTransactionType.TransferIn,
                    WalletTransactionType.TransferOut
                },
                "ADJUSTMENT" => new[]
                {
                    WalletTransactionType.AdjustmentCredit,
                    WalletTransactionType.AdjustmentDebit
                },
                "AUCTION" => new[]
                {
                    WalletTransactionType.AuctionHoldCapture,
                    WalletTransactionType.AuctionForfeitCredit,
                    WalletTransactionType.AuctionDepositRefund
                },
                _ when Enum.TryParse<WalletTransactionType>(request.Type.Trim(), true, out var txType) =>
                    new[] { txType },
                _ => throw new ArgumentException("Transaction type is invalid.", nameof(request))
            };

            if (types.Length > 0)
                query = query.Where(t => types.Contains(t.Type));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!request.Status.TryToEnum<TransactionStatus>(out var status))
            {
                throw new ArgumentException("Transaction status is invalid.", nameof(request));
            }

            query = query.Where(t => t.Status == status);
        }

        if (request.ReferenceId.HasValue)
        {
            if (request.ReferenceId.Value == Guid.Empty)
            {
                throw new ArgumentException("Reference ID must be a valid UUID.", nameof(request));
            }

            var refId = request.ReferenceId.Value;
            query = query.Where(t => t.ReferenceId == refId);
        }

        if (request.FromDate.HasValue)
        {
            var fromUtc = request.FromDate.Value.ToUniversalTime();
            query = query.Where(t => t.CreatedAt >= fromUtc);
        }

        if (request.ToDate.HasValue)
        {
            var toUtc = request.ToDate.Value.ToUniversalTime();
            query = query.Where(t => t.CreatedAt <= toUtc);
        }

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
                t.Currency ?? "Vnd",
                t.BalanceAfter,
                t.Status.ToString(),
                t.Note ?? string.Empty,
                t.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PagedData<WalletTransactionResponse>(items, PaginationMetadata.Create(page, pageSize, total));
    }
}
