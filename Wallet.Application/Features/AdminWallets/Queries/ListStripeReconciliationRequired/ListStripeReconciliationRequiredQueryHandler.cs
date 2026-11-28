namespace Wallet.Application.Features.AdminWallets.Queries.ListStripeReconciliationRequired;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ListStripeReconciliationRequiredQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListStripeReconciliationRequiredQuery, PagedData<StripeReconciliationAttemptItem>>
{
    public async Task<PagedData<StripeReconciliationAttemptItem>> Handle(
        ListStripeReconciliationRequiredQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = context.StripePaymentAttempts
            .AsNoTracking()
            .Where(item => item.Status == StripePaymentAttemptStatus.ReconciliationRequired);

        if (!string.IsNullOrWhiteSpace(request.Purpose))
        {
            if (!request.Purpose.TryToEnum<StripePaymentPurpose>(out var purposeFilter))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_PURPOSE", "Stripe payment purpose filter is invalid.");
            }

            query = query.Where(item => item.Purpose == purposeFilter);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(item => new StripeReconciliationAttemptItem(
            item.Id,
            item.Purpose.ToString(),
            item.OwnerId,
            item.WalletTopupId,
            item.DirectPaymentOrderId,
            item.Amount,
            item.CheckoutSessionId,
            item.PaymentIntentId,
            item.Status.ToString(),
            item.BlocksReplacement,
            item.CreatedAt,
            item.UpdatedAt)).ToArray();

        return new PagedData<StripeReconciliationAttemptItem>(items, PaginationMetadata.Create(page, pageSize, total));
    }
}
