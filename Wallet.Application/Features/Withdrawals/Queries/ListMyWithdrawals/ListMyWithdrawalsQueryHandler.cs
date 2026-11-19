namespace Wallet.Application.Features.Withdrawals.Queries.ListMyWithdrawals;

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

internal sealed class ListMyWithdrawalsQueryHandler(IWalletDbContext context)
    : IRequestHandler<ListMyWithdrawalsQuery, PagedData<WithdrawalSummaryResponse>>
{
    public async Task<PagedData<WithdrawalSummaryResponse>> Handle(
        ListMyWithdrawalsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationConstants.Normalize(request.Page, request.PageSize);
        var query = from withdrawal in context.WithdrawalRequests.AsNoTracking()
                    join wallet in context.Wallets.AsNoTracking() on withdrawal.WalletId equals wallet.Id
                    where wallet.UserId == request.UserId
                    select withdrawal;

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!request.Status.TryToEnum<WithdrawalStatus>(out var status))
            {
                throw new BusinessRuleValidationException("VAL_INVALID_STATUS", "Withdrawal status filter is invalid.");
            }

            query = query.Where(item => item.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedData<WithdrawalSummaryResponse>(
            items.Select(WithdrawalReadMapper.ToSummary).ToArray(),
            PaginationMetadata.Create(page, pageSize, total));
    }
}
