namespace Wallet.Application.Features.Stripe;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed record StripePreparedTopup(Guid TopupId, string PaymentUrl, decimal Amount, DateTimeOffset CreatedAt);

internal static class StripeTopupPreparation
{
    internal static async Task<StripePreparedTopup> CreateAsync(
        IWalletDbContext context, IStripeCheckoutClient provider, Guid ownerId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);
        if (ownerId == Guid.Empty || amount is < 10000 or > StripeAmountLimits.MaximumVnd || decimal.Truncate(amount) != amount ||
            string.IsNullOrEmpty(idempotencyKey) || idempotencyKey.Length > 128 ||
            idempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Topup request is invalid.");

        var (topup, attempt, replay) = await PrepareAsync(context, ownerId, amount, idempotencyKey, cancellationToken);
        if (replay)
            return new StripePreparedTopup(topup.Id, attempt.CheckoutUrl!, topup.Amount, topup.CreatedAt);

        var session = await provider.CreateSessionAsync(
            new StripeCheckoutRequest(attempt.Id, decimal.ToInt64(amount), attempt.ProviderIdempotencyKey),
            cancellationToken);
        StripeCheckoutSessionValidation.Validate(session);

        context.EnsureNoPendingChanges();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var lockedTopup = await context.LockTopupByIdAsync(topup.Id, cancellationToken)
            ?? throw new InvalidOperationException("Stripe topup was not found.");
        await context.ReloadUnmodifiedAsync(lockedTopup, cancellationToken);
        var lockedAttempt = await context.LockStripePaymentAttemptAsync(attempt.Id, cancellationToken)
            ?? throw new InvalidOperationException("Stripe payment attempt was not found.");
        await context.ReloadUnmodifiedAsync(lockedAttempt, cancellationToken);
        if (lockedAttempt.OwnerId != ownerId || lockedAttempt.WalletTopupId != lockedTopup.Id ||
            lockedAttempt.Amount != amount || lockedAttempt.Status != StripePaymentAttemptStatus.CreationPending ||
            lockedTopup.Status != TopupStatus.Pending || !lockedAttempt.BlocksReplacement ||
            (lockedAttempt.CheckoutSessionId is not null && lockedAttempt.CheckoutSessionId != session.SessionId))
            throw Conflict();

        lockedAttempt.CheckoutSessionId = session.SessionId;
        lockedAttempt.CheckoutUrl = session.PaymentUrl;
        lockedAttempt.SessionExpiresAt = session.ExpiresAt;
        lockedAttempt.Status = StripePaymentAttemptStatus.AwaitingPayment;
        lockedAttempt.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StripePreparedTopup(lockedTopup.Id, session.PaymentUrl, lockedTopup.Amount, lockedTopup.CreatedAt);
    }

    private static async Task<(WalletTopup Topup, StripePaymentAttempt Attempt, bool Replay)> PrepareAsync(
        IWalletDbContext context, Guid ownerId, decimal amount, string key, CancellationToken token)
    {
        context.EnsureNoPendingChanges();
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var wallet = await context.LockWalletByUserIdAsync(ownerId, token)
            ?? throw new WalletNotFoundException(ownerId.ToString());
        await context.ReloadUnmodifiedAsync(wallet, token);
        if (wallet.UserId != ownerId || wallet.Currency != CurrencyCode.Vnd)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Topup requires a VND wallet.");

        var fingerprint = Fingerprint(ownerId, amount);
        var existing = await context.StripePaymentAttempts.AsNoTracking().SingleOrDefaultAsync(
            item => item.OwnerId == ownerId && item.ClientIdempotencyKey == key, token);
        if (existing is not null)
        {
            if (existing.Purpose != StripePaymentPurpose.Topup || existing.Amount != amount ||
                existing.RequestFingerprint != fingerprint || existing.WalletTopupId is not Guid topupId ||
                existing.Status != StripePaymentAttemptStatus.AwaitingPayment || !existing.BlocksReplacement ||
                string.IsNullOrWhiteSpace(existing.CheckoutUrl) || existing.SessionExpiresAt <= DateTimeOffset.UtcNow ||
                existing.SessionExpiresAt is null)
                throw Conflict();
            var previous = await context.WalletTopups.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == topupId && item.WalletId == wallet.Id, token);
            if (previous is null || previous.Status != TopupStatus.Pending || previous.Amount != amount)
                throw Conflict();
            await transaction.CommitAsync(token);
            return (previous, existing, true);
        }

        var topup = new WalletTopup
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, Amount = amount, Status = TopupStatus.Pending
        };
        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, Purpose = StripePaymentPurpose.Topup, WalletTopupId = topup.Id,
            Amount = amount, RequestFingerprint = fingerprint, ClientIdempotencyKey = key,
            ProviderIdempotencyKey = $"wallet_topup_{Guid.NewGuid():N}"
        };
        context.WalletTopups.Add(topup);
        context.StripePaymentAttempts.Add(attempt);
        context.AddAuditLog(AuditAction.TopupRequest, nameof(WalletTopup), topup.Id.ToString(),
            newValues: new { topup.Amount, topup.Status, AttemptId = attempt.Id }, userId: ownerId.ToString());
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return (topup, attempt, false);
    }

    private static string Fingerprint(Guid ownerId, decimal amount)
    {
        var value = $"topup\n{ownerId:D}\n{amount.ToString("0", CultureInfo.InvariantCulture)}\nVND";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static BusinessRuleValidationException Conflict() =>
        new("PAYMENT_CONFLICT", "The payment request conflicts with an existing attempt.", 409);
}
