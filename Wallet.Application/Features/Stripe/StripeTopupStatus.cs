namespace Wallet.Application.Features.Stripe;

using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed record StripeTopupStatusResult(
    Guid TransactionId, decimal Amount, string Currency, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

internal static class StripeTopupStatus
{
    internal static async Task<StripeTopupStatusResult> GetAsync(
        IWalletDbContext context, Guid ownerId, Guid topupId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ownerId == Guid.Empty || topupId == Guid.Empty)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Topup status request is invalid.");

        var result = await (
            from topup in context.WalletTopups.AsNoTracking()
            join wallet in context.Wallets.AsNoTracking() on topup.WalletId equals wallet.Id
            where topup.Id == topupId && wallet.UserId == ownerId && topup.VnpTxnRef == null &&
                  context.StripePaymentAttempts.AsNoTracking().Any(attempt =>
                      attempt.WalletTopupId == topup.Id && attempt.OwnerId == ownerId &&
                      attempt.Purpose == StripePaymentPurpose.Topup)
            select new { topup.Id, topup.Amount, wallet.Currency, topup.Status, topup.CreatedAt, topup.UpdatedAt }
        ).SingleOrDefaultAsync(cancellationToken);

        if (result is null)
            throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "Topup", topupId);

        return new StripeTopupStatusResult(
            result.Id, result.Amount, result.Currency.ToString(), result.Status.ToString(),
            result.CreatedAt, result.UpdatedAt);
    }
}
