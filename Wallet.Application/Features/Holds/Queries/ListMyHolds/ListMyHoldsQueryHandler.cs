namespace Wallet.Application.Features.Holds.Queries.ListMyHolds;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ListMyHoldsQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListMyHoldsQuery, PagedData<HoldListItem>>
{
    public async Task<PagedData<HoldListItem>> Handle(ListMyHoldsQuery request, CancellationToken cancellationToken)
    {
        if (request.ValidateRequest().Any())
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The hold list request is invalid.");
        var query = context.AuctionHolds.AsNoTracking().Where(item => item.OwnerUserId == request.UserId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!request.Status.TryToEnum<AuctionHoldStatus>(out var status))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The hold list request is invalid.");
            }

            query = query.Where(item => item.Status == status);
        }
        var total = await query.CountAsync(cancellationToken);
        var page = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArrayAsync(cancellationToken);
        var items = page.Select(item => new HoldListItem(
            item.Id,
            item.ReferenceType,
            item.ReferenceId,
            HoldOperations.ExtractAuctionId(item.MetadataJson),
            item.Amount,
            item.Status.ToString(),
            item.CreatedAt,
            item.ResolvedAt)).ToArray();
        return new PagedData<HoldListItem>(items, PaginationMetadata.Create(request.Page, request.PageSize, total));
    }
}
