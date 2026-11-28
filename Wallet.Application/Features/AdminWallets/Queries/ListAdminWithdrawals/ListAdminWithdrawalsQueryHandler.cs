namespace Wallet.Application.Features.AdminWallets.Queries.ListAdminWithdrawals;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Features.Withdrawals;
using Wallet.Application.Interfaces;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ListAdminWithdrawalsQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListAdminWithdrawalsQuery, PagedData<AdminWithdrawalResponse>>
{
    public async Task<PagedData<AdminWithdrawalResponse>> Handle(
        ListAdminWithdrawalsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = from withdrawal in context.WithdrawalRequests.AsNoTracking()
                    join wallet in context.Wallets.AsNoTracking() on withdrawal.WalletId equals wallet.Id
                    select new { Withdrawal = withdrawal, OwnerUserId = wallet.UserId };

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!request.Status.TryToEnum<WithdrawalStatus>(out var status))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_STATUS", "Withdrawal status filter is invalid.");
            }

            query = query.Where(item => item.Withdrawal.Status == status);
        }

        if (request.UserId.HasValue)
        {
            var userId = request.UserId.Value;
            query = query.Where(item => item.OwnerUserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(request.TransferReference))
        {
            var transferReference = request.TransferReference.Trim();
            query = query.Where(item => item.Withdrawal.GatewayReferenceId == transferReference);
        }

        if (request.FromDate.HasValue)
        {
            var fromUtc = request.FromDate.Value.ToUniversalTime();
            query = query.Where(item => item.Withdrawal.CreatedAt >= fromUtc);
        }

        if (request.ToDate.HasValue)
        {
            var toUtc = request.ToDate.Value.ToUniversalTime();
            query = query.Where(item => item.Withdrawal.CreatedAt <= toUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var pageItems = await query
            .OrderByDescending(item => item.Withdrawal.CreatedAt)
            .ThenByDescending(item => item.Withdrawal.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedData<AdminWithdrawalResponse>(
            pageItems.Select(x => WithdrawalReadMapper.ToAdminSummary(x.Withdrawal, x.OwnerUserId)).ToArray(),
            PaginationMetadata.Create(page, pageSize, total));
    }
}
