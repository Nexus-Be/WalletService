namespace Wallet.Application.Features.Wallets.Queries.GetTransactionById;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class GetTransactionByIdQueryHandler(IWalletDbContext context)
    : IRequestHandler<GetTransactionByIdQuery, WalletTransactionDetailResponse>
{
    public async Task<WalletTransactionDetailResponse> Handle(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var found = await (
            from item in context.WalletTransactions.AsNoTracking()
            join wallet in context.Wallets.AsNoTracking() on item.WalletId equals wallet.Id
            where item.Id == request.TransactionId && wallet.UserId == request.UserId
            select new { Transaction = item, WalletCurrency = wallet.Currency }
        ).SingleOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("TRANSACTION_NOT_FOUND", nameof(WalletTransaction), request.TransactionId);

        var tx = found.Transaction;
        string? gatewayStatus = null;
        string? gatewayReference = null;

        if (tx.ReferenceId is Guid refId)
        {
            if (tx.Type is WalletTransactionType.TopupStripe or WalletTransactionType.TopupVnpay)
            {
                var topup = await context.WalletTopups.AsNoTracking()
                    .Where(item => item.Id == refId && item.WalletId == tx.WalletId)
                    .Select(item => new { item.Id, item.Status, item.VnpTransactionNo, item.VnpTxnRef })
                    .FirstOrDefaultAsync(cancellationToken);
                if (topup is not null)
                {
                    gatewayStatus = topup.Status.ToString();
                    gatewayReference = topup.VnpTransactionNo
                        ?? topup.VnpTxnRef
                        ?? await context.StripePaymentAttempts.AsNoTracking()
                            .Where(item => item.WalletTopupId == topup.Id)
                            .OrderByDescending(item => item.Status == StripePaymentAttemptStatus.Successful)
                            .ThenByDescending(item => item.CreatedAt)
                            .Select(item => item.PaymentIntentId)
                            .FirstOrDefaultAsync(cancellationToken);
                }
            }
            else if (tx.Type == WalletTransactionType.DirectGatewayPayment)
            {
                var directOrder = await context.DirectPaymentOrders.AsNoTracking()
                    .Where(item => item.OrderId == refId)
                    .Select(item => new { item.Id, item.Status, item.VnpTransactionNo })
                    .FirstOrDefaultAsync(cancellationToken);
                if (directOrder is not null)
                {
                    gatewayStatus = directOrder.Status.ToString();
                    gatewayReference = directOrder.VnpTransactionNo
                        ?? await context.StripePaymentAttempts.AsNoTracking()
                            .Where(item => item.DirectPaymentOrderId == directOrder.Id)
                            .OrderByDescending(item => item.Status == StripePaymentAttemptStatus.Successful)
                            .ThenByDescending(item => item.CreatedAt)
                            .Select(item => item.PaymentIntentId)
                            .FirstOrDefaultAsync(cancellationToken);
                }
            }
            else if (tx.Type is WalletTransactionType.WithdrawalDebit or WalletTransactionType.WithdrawalReversal)
            {
                var withdrawal = await context.WithdrawalRequests.AsNoTracking()
                    .Where(item => item.Id == refId && item.WalletId == tx.WalletId)
                    .Select(item => new { item.Status, item.GatewayReferenceId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (withdrawal is not null)
                {
                    gatewayStatus = withdrawal.Status.ToString();
                    gatewayReference = withdrawal.GatewayReferenceId;
                }
            }
            else if (tx.Type is WalletTransactionType.AuctionHoldCapture or WalletTransactionType.AuctionForfeitCredit or WalletTransactionType.AuctionDepositRefund)
            {
                var hold = await context.AuctionHolds.AsNoTracking()
                    .Where(item => item.Id == refId)
                    .Select(item => new { item.Status, item.ReferenceId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (hold is not null)
                {
                    gatewayStatus = hold.Status.ToString();
                    gatewayReference = hold.ReferenceId;
                }
            }
        }

        return new WalletTransactionDetailResponse(
            tx.Id.ToString(),
            tx.WalletId.ToString(),
            tx.UserId ?? request.UserId.ToString(),
            tx.Type.ToString(),
            tx.Amount,
            tx.BalanceBefore,
            tx.BalanceAfter,
            tx.Currency ?? found.WalletCurrency.ToString(),
            tx.Status.ToString(),
            tx.ReferenceId,
            tx.Note,
            gatewayStatus,
            gatewayReference,
            tx.CreatedAt,
            tx.UpdatedAt);
    }
}
