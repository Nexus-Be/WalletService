namespace Wallet.Application.Features.Topups.Queries.ListTopups;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ListTopupsQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListTopupsQuery, PagedData<TopupListItem>>
{
    public async Task<PagedData<TopupListItem>> Handle(ListTopupsQuery request, CancellationToken cancellationToken)
    {
        var query = from topup in context.WalletTopups.AsNoTracking()
                    join wallet in context.Wallets.AsNoTracking() on topup.WalletId equals wallet.Id
                    where wallet.UserId == request.UserId && topup.VnpTxnRef == null &&
                          context.StripePaymentAttempts.AsNoTracking().Any(attempt =>
                              attempt.WalletTopupId == topup.Id && attempt.OwnerId == request.UserId &&
                              attempt.Purpose == StripePaymentPurpose.Topup)
                    select new { Topup = topup, wallet.Currency };

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!request.Status.TryToEnum<TopupStatus>(out var statusFilter))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_STATUS", "Topup status filter is invalid.");
            }

            query = query.Where(item => item.Topup.Status == statusFilter);
        }
        var total = await query.CountAsync(cancellationToken);
        var page = await query.OrderByDescending(item => item.Topup.CreatedAt)
            .ThenByDescending(item => item.Topup.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var ids = page.Select(item => item.Topup.Id).ToArray();
        var attempts = await context.StripePaymentAttempts.AsNoTracking()
            .Where(item => item.OwnerId == request.UserId && item.WalletTopupId.HasValue &&
                           ids.Contains(item.WalletTopupId.Value) && item.Purpose == StripePaymentPurpose.Topup)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var items = page.Select(item =>
        {
            var attempt = attempts.Where(value => value.WalletTopupId == item.Topup.Id)
                .OrderByDescending(value => value.CreatedAt).FirstOrDefault();
            var canResume = item.Topup.Status == TopupStatus.Pending &&
                attempt?.Status == StripePaymentAttemptStatus.AwaitingPayment &&
                attempt.SessionExpiresAt > now && !string.IsNullOrWhiteSpace(attempt.CheckoutUrl);
            return new TopupListItem(item.Topup.Id, item.Topup.Amount, item.Currency.ToString(),
                item.Topup.Status.ToString(), item.Topup.CreatedAt, item.Topup.UpdatedAt,
                canResume, attempt?.SessionExpiresAt);
        }).ToArray();
        return new PagedData<TopupListItem>(items,
            PaginationMetadata.Create(request.Page, request.PageSize, total));
    }
}
