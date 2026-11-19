namespace Wallet.Application.Features.AdminWallets.Queries.ListAdminHolds;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Features.Holds;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ListAdminHoldsQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListAdminHoldsQuery, PagedData<AdminHoldListItem>>
{
    public async Task<PagedData<AdminHoldListItem>> Handle(
        ListAdminHoldsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = context.AuctionHolds.AsNoTracking();

        if (request.UserId.HasValue)
        {
            query = query.Where(item => item.OwnerUserId == request.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!request.Status.TryToEnum<AuctionHoldStatus>(out var statusFilter))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_STATUS", "Hold status filter is invalid.");
            }

            query = query.Where(item => item.Status == statusFilter);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(item => new AdminHoldListItem(
            item.Id,
            item.WalletId,
            item.OwnerUserId,
            item.ReferenceType,
            item.ReferenceId,
            HoldOperations.ExtractAuctionId(item.MetadataJson),
            item.Amount,
            item.Status.ToString(),
            item.CreatedAt,
            item.ResolvedAt)).ToArray();

        return new PagedData<AdminHoldListItem>(items, PaginationMetadata.Create(page, pageSize, total));
    }
}
