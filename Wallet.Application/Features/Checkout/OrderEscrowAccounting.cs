namespace Wallet.Application.Features.Checkout;

using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

internal sealed record OrderEscrowLookup(
    OrderEscrow? Escrow,
    WalletTransaction? Payment,
    bool HasPayment,
    bool HasResolvedOperation,
    bool WasReconciled);

internal static class OrderEscrowAccounting
{
    public static async Task<OrderEscrowLookup> LoadOrReconcileAsync(
        IWalletDbContext context,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var escrow = await context.OrderEscrows
            .SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        var payments = await context.WalletTransactions
            .AsNoTracking()
            .Where(item => item.ReferenceId == orderId &&
                           item.Type == WalletTransactionType.PurchaseDebit &&
                           item.Status == TransactionStatus.Success)
            .ToListAsync(cancellationToken);

        if (payments.Count > 1)
        {
            if (escrow is not null)
            {
                throw new InvalidOperationException("Order escrow has multiple matching wallet payment transactions.");
            }

            var resolved = await HasResolvedOperation(context, orderId, cancellationToken);
            return new OrderEscrowLookup(null, null, true, resolved, false);
        }

        var payment = payments.SingleOrDefault();
        if (escrow is not null)
        {
            if (payment is null ||
                escrow.WalletId != payment.WalletId ||
                escrow.OriginalAmount != payment.Amount)
            {
                throw new InvalidOperationException("Order escrow does not match its wallet payment transaction.");
            }

            return new OrderEscrowLookup(escrow, payment, true, false, false);
        }

        if (payment is null)
        {
            return new OrderEscrowLookup(null, null, false, false, false);
        }

        if (await HasResolvedOperation(context, orderId, cancellationToken))
        {
            return new OrderEscrowLookup(null, payment, true, true, false);
        }

        escrow = new OrderEscrow
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            WalletId = payment.WalletId,
            OriginalAmount = payment.Amount,
            RemainingAmount = payment.Amount
        };
        context.OrderEscrows.Add(escrow);
        context.AddAuditLog(
            AuditAction.OrderEscrowReconciliation,
            nameof(OrderEscrow),
            escrow.Id.ToString(),
            newValues: new
            {
                escrow.OrderId,
                escrow.WalletId,
                escrow.OriginalAmount,
                escrow.RemainingAmount,
                SourceTransactionId = payment.Id
            },
            userId: payment.UserId);
        return new OrderEscrowLookup(escrow, payment, true, false, true);
    }

    private static Task<bool> HasResolvedOperation(
        IWalletDbContext context,
        Guid orderId,
        CancellationToken cancellationToken) =>
        context.WalletTransactions.AsNoTracking().AnyAsync(
            item => item.ReferenceId == orderId &&
                    item.Status == TransactionStatus.Success &&
                    (item.Type == WalletTransactionType.EscrowRelease ||
                     item.Type == WalletTransactionType.RefundCredit),
            cancellationToken);
}
