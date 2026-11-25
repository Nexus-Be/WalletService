namespace Wallet.Application.Features.Checkout.Commands.ResumeDirectPayment;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ResumeDirectPaymentCommandHandler(
    IWalletDbContext context,
    IStripeCheckoutClient provider)
    : IRequestHandler<ResumeDirectPaymentCommand, ResumeDirectPaymentResponse>
{
    public async Task<ResumeDirectPaymentResponse> Handle(
        ResumeDirectPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var order = await context.DirectPaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OrderId == request.OrderId && item.VnpTxnRef == null,
                cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", request.OrderId);

        var attempt = await context.StripePaymentAttempts
            .AsNoTracking()
            .Where(item => item.OwnerId == request.UserId &&
                           item.DirectPaymentOrderId == order.Id &&
                           item.Purpose == StripePaymentPurpose.DirectOrder)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "DirectPayment", request.OrderId);

        var wallet = await context.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);
        wallet?.EnsureActive();

        if (order.Status != TopupStatus.Pending ||
            attempt.Status != StripePaymentAttemptStatus.AwaitingPayment ||
            attempt.SessionExpiresAt is null ||
            attempt.SessionExpiresAt <= DateTimeOffset.UtcNow ||
            string.IsNullOrWhiteSpace(attempt.CheckoutUrl) ||
            string.IsNullOrWhiteSpace(attempt.CheckoutSessionId))
        {
            throw Conflict();
        }

        var session = await provider.GetSessionAsync(attempt.CheckoutSessionId, cancellationToken);
        if (session.SessionId != attempt.CheckoutSessionId ||
            session.SessionStatus != "open" ||
            session.PaymentStatus != "unpaid" ||
            session.PaymentIntentId is not null ||
            session.AmountVnd != decimal.ToInt64(order.Amount) ||
            session.AttemptReference != attempt.Id.ToString("D") ||
            session.ClientReference != attempt.Id.ToString("D"))
        {
            throw Conflict();
        }

        return new ResumeDirectPaymentResponse(order.OrderId, attempt.CheckoutUrl, attempt.SessionExpiresAt.Value);
    }

    private static BusinessRuleValidationException Conflict() =>
        new("PAYMENT_CONFLICT", "The checkout session is no longer available to resume.", 409);
}
