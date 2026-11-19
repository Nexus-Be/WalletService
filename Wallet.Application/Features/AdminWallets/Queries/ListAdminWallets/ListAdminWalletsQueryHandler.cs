namespace Wallet.Application.Features.AdminWallets.Queries.ListAdminWallets;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using Wallet.Application.Interfaces;
using Wallet.Domain.Exceptions;

internal sealed class ListAdminWalletsQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListAdminWalletsQuery, PagedData<WalletResponse>>
{
    public async Task<PagedData<WalletResponse>> Handle(
        ListAdminWalletsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = context.Wallets.AsNoTracking();

        if (request.UserId.HasValue)
        {
            query = query.Where(item => item.UserId == request.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = request.Status.Trim().ToUpperInvariant();
            if (normalizedStatus is not ("ACTIVE" or "FROZEN"))
            {
                throw new BusinessRuleValidationException(
                    "VAL_INVALID_STATUS",
                    "Wallet status filter must be 'Active' or 'Frozen'.");
            }

            query = query.Where(item => item.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            if (Guid.TryParse(search, out var searchGuid))
            {
                query = query.Where(item => item.UserId == searchGuid || item.Id == searchGuid);
            }
            else
            {
                var upper = search.ToUpperInvariant();
                query = query.Where(item => item.Status == upper);
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(wallet => new WalletResponse(
            wallet.Id.ToString(),
            wallet.UserId.ToString(),
            wallet.Balance,
            wallet.AvailableBalance,
            wallet.LockedBalance,
            wallet.Currency.ToString(),
            wallet.Status.ToApiStatus(),
            wallet.UpdatedAt)).ToArray();

        return new PagedData<WalletResponse>(items, PaginationMetadata.Create(page, pageSize, total));
    }
}
