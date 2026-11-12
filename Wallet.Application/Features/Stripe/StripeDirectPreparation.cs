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

internal sealed record StripePreparedDirect(Guid TransactionId, Guid OrderId, string PaymentUrl, decimal Amount,
    DateTimeOffset CreatedAt);

internal static class StripeDirectPreparation
{
    internal static async Task<StripePreparedDirect> CreateAsync(
        IWalletDbContext context, IStripeCheckoutClient provider, Guid ownerId, Guid orderId, decimal amount,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);
        if (ownerId == Guid.Empty || orderId == Guid.Empty || amount <= 0 ||
            amount > StripeAmountLimits.MaximumVnd || decimal.Truncate(amount) != amount)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Direct payment request is invalid.");

        var (order, ledger, attempt, replay) = await PrepareAsync(context, ownerId, orderId, amount, token);
        if (replay)
            return new StripePreparedDirect(ledger.Id, orderId, attempt.CheckoutUrl!, amount, ledger.CreatedAt);

        var session = await provider.CreateSessionAsync(
            new StripeCheckoutRequest(attempt.Id, decimal.ToInt64(amount), attempt.ProviderIdempotencyKey), token);
        StripeCheckoutSessionValidation.Validate(session);
        context.EnsureNoPendingChanges();
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var lockedOrder = await context.LockDirectPaymentOrderByIdAsync(order.Id, token)
            ?? throw new InvalidOperationException("Stripe direct payment order was not found.");
        await context.ReloadUnmodifiedAsync(lockedOrder, token);
        var lockedAttempt = await context.LockStripePaymentAttemptAsync(attempt.Id, token)
            ?? throw new InvalidOperationException("Stripe payment attempt was not found.");
        await context.ReloadUnmodifiedAsync(lockedAttempt, token);
        if (lockedOrder.OrderId != orderId || lockedOrder.Status != TopupStatus.Pending ||
            lockedOrder.Amount != amount || lockedAttempt.OwnerId != ownerId ||
            lockedAttempt.DirectPaymentOrderId != lockedOrder.Id || lockedAttempt.Amount != amount ||
            lockedAttempt.Status != StripePaymentAttemptStatus.CreationPending || !lockedAttempt.BlocksReplacement ||
            (lockedAttempt.CheckoutSessionId is not null && lockedAttempt.CheckoutSessionId != session.SessionId))
            throw Conflict();

        lockedAttempt.CheckoutSessionId = session.SessionId;
        lockedAttempt.CheckoutUrl = session.PaymentUrl;
        lockedAttempt.SessionExpiresAt = session.ExpiresAt;
        lockedAttempt.Status = StripePaymentAttemptStatus.AwaitingPayment;
        lockedAttempt.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new StripePreparedDirect(ledger.Id, orderId, session.PaymentUrl, amount, ledger.CreatedAt);
    }

    private static async Task<(DirectPaymentOrder Order, WalletTransaction Ledger, StripePaymentAttempt Attempt, bool Replay)>
        PrepareAsync(IWalletDbContext context, Guid ownerId, Guid orderId, decimal amount, CancellationToken token)
    {
        context.EnsureNoPendingChanges();
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.LockOrderPaymentAsync(orderId, token);
        var parentId = await context.DirectPaymentOrders.AsNoTracking().Where(item => item.OrderId == orderId)
            .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(token);
        DirectPaymentOrder? order = null;
        if (parentId is Guid id)
        {
            order = await context.LockDirectPaymentOrderByIdAsync(id, token)
                ?? throw new InvalidOperationException("Stripe direct payment order was not found.");
            await context.ReloadUnmodifiedAsync(order, token);
        }
        var wallet = await context.LockWalletByUserIdAsync(ownerId, token)
            ?? throw new WalletNotFoundException(ownerId.ToString());
        await context.ReloadUnmodifiedAsync(wallet, token);
        if (wallet.UserId != ownerId || wallet.Currency != CurrencyCode.Vnd)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Direct payment requires a VND wallet.");

        if (await context.WalletTransactions.AnyAsync(item => item.ReferenceId == orderId &&
            item.Type == WalletTransactionType.PurchaseDebit &&
            item.Status != TransactionStatus.Failed && item.Status != TransactionStatus.Cancelled, token))
            throw Conflict();
        var ledger = await context.WalletTransactions.SingleOrDefaultAsync(item => item.ReferenceId == orderId &&
            item.Type == WalletTransactionType.DirectGatewayPayment, token);
        var fingerprint = Fingerprint(ownerId, orderId, amount);
        if (order is null)
        {
            if (ledger is not null)
                throw Conflict();
            order = new DirectPaymentOrder
            {
                Id = Guid.NewGuid(), OrderId = orderId, Amount = amount, Status = TopupStatus.Pending,
                RequestFingerprint = fingerprint
            };
            ledger = new WalletTransaction
            {
                Id = Guid.NewGuid(), WalletId = wallet.Id, UserId = ownerId.ToString(), ReferenceId = orderId,
                Type = WalletTransactionType.DirectGatewayPayment, Amount = amount, Currency = wallet.Currency.ToString(),
                BalanceBefore = wallet.Balance, BalanceAfter = wallet.Balance, Status = TransactionStatus.Pending,
                Note = $"Payment for order {orderId:D} via Stripe"
            };
            context.DirectPaymentOrders.Add(order);
            context.WalletTransactions.Add(ledger);
        }
        else
        {
            if (ledger is null)
                throw Conflict();
            await context.ReloadUnmodifiedAsync(ledger, token);
            if (order.OrderId != orderId || order.VnpTxnRef is not null || order.Amount != amount ||
                order.RequestFingerprint != fingerprint || ledger.WalletId != wallet.Id ||
                ledger.UserId != ownerId.ToString() || ledger.ReferenceId != orderId || ledger.Amount != amount ||
                ledger.Type != WalletTransactionType.DirectGatewayPayment)
                throw Conflict();
            var attempts = await context.StripePaymentAttempts.AsNoTracking()
                .Where(item => item.DirectPaymentOrderId == order.Id).ToListAsync(token);
            if (attempts.Count == 0 || attempts.Any(item => item.OwnerId != ownerId || item.Amount != amount ||
                item.RequestFingerprint != fingerprint || item.Status == StripePaymentAttemptStatus.ReconciliationRequired))
                throw Conflict();
            var active = attempts.SingleOrDefault(item => item.BlocksReplacement);
            if (active is not null)
            {
                if (order.Status != TopupStatus.Pending || ledger.Status != TransactionStatus.Pending ||
                    active.Status != StripePaymentAttemptStatus.AwaitingPayment ||
                    string.IsNullOrWhiteSpace(active.CheckoutUrl) || active.SessionExpiresAt is null ||
                    active.SessionExpiresAt <= DateTimeOffset.UtcNow)
                    throw Conflict();
                await transaction.CommitAsync(token);
                return (order, ledger, active, true);
            }
            if (order.Status is not (TopupStatus.Failed or TopupStatus.Expired) ||
                ledger.Status != TransactionStatus.Failed ||
                attempts.Any(item => item.Status is not (StripePaymentAttemptStatus.Failed or StripePaymentAttemptStatus.Expired)))
                throw Conflict();
            order.Status = TopupStatus.Pending;
            order.UpdatedAt = DateTimeOffset.UtcNow;
            ledger.Status = TransactionStatus.Pending;
            ledger.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var attempt = new StripePaymentAttempt
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, Purpose = StripePaymentPurpose.DirectOrder,
            DirectPaymentOrderId = order.Id, Amount = amount, RequestFingerprint = fingerprint,
            ProviderIdempotencyKey = $"wallet_direct_{Guid.NewGuid():N}"
        };
        context.StripePaymentAttempts.Add(attempt);
        context.AddAuditLog(AuditAction.DirectPaymentRequest, nameof(DirectPaymentOrder), order.Id.ToString(),
            newValues: new { order.OrderId, order.Amount, order.Status, AttemptId = attempt.Id },
            userId: ownerId.ToString());
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return (order, ledger, attempt, false);
    }

    private static string Fingerprint(Guid ownerId, Guid orderId, decimal amount)
    {
        var value = $"direct\n{ownerId:D}\n{orderId:D}\n{amount.ToString("0", CultureInfo.InvariantCulture)}\nVND";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static BusinessRuleValidationException Conflict() =>
        new("PAYMENT_CONFLICT", "The payment request conflicts with an existing attempt.", 409);
}
