namespace Wallet.Application.Features.Topups.Commands.ResumeTopup;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ResumeTopupCommandHandler(IWalletDbContext context, IStripeCheckoutClient provider)
    : IRequestHandler<ResumeTopupCommand, ResumeTopupResponse>
{
    public async Task<ResumeTopupResponse> Handle(ResumeTopupCommand request, CancellationToken cancellationToken)
    {
        var found = await (from item in context.WalletTopups.AsNoTracking()
                           join wallet in context.Wallets.AsNoTracking() on item.WalletId equals wallet.Id
                           where item.Id == request.TransactionId && wallet.UserId == request.UserId &&
                                 item.VnpTxnRef == null
                           select new { Topup = item, Wallet = wallet }).SingleOrDefaultAsync(cancellationToken);
        if (found is null)
            throw new EntityNotFoundException("PAYMENT_NOT_FOUND", "Topup", request.TransactionId);

        var topup = found.Topup;
        found.Wallet.EnsureActive();

        var attempt = await context.StripePaymentAttempts.AsNoTracking()
            .Where(item => item.OwnerId == request.UserId && item.WalletTopupId == topup.Id &&
                           item.Purpose == StripePaymentPurpose.Topup)
            .OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (topup.Status != TopupStatus.Pending || attempt?.Status != StripePaymentAttemptStatus.AwaitingPayment ||
            attempt.SessionExpiresAt is null || attempt.SessionExpiresAt <= DateTimeOffset.UtcNow ||
            string.IsNullOrWhiteSpace(attempt.CheckoutUrl) || string.IsNullOrWhiteSpace(attempt.CheckoutSessionId))
            throw Conflict();

        var session = await provider.GetSessionAsync(attempt.CheckoutSessionId, cancellationToken);
        if (session.SessionId != attempt.CheckoutSessionId || session.SessionStatus != "open" ||
            session.PaymentStatus != "unpaid" || session.PaymentIntentId is not null ||
            session.AmountVnd != decimal.ToInt64(topup.Amount) ||
            session.AttemptReference != attempt.Id.ToString("D") ||
            session.ClientReference != attempt.Id.ToString("D"))
            throw Conflict();

        return new ResumeTopupResponse(topup.Id, attempt.CheckoutUrl, attempt.SessionExpiresAt.Value);
    }

    private static BusinessRuleValidationException Conflict() =>
        new("PAYMENT_CONFLICT", "The checkout session is no longer available to resume.", 409);
}
