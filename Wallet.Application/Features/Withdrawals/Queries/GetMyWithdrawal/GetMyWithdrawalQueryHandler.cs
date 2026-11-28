namespace Wallet.Application.Features.Withdrawals.Queries.GetMyWithdrawal;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Exceptions;

internal sealed class GetMyWithdrawalQueryHandler(IWalletDbContext context)
    : IRequestHandler<GetMyWithdrawalQuery, WithdrawalSummaryResponse>
{
    public async Task<WithdrawalSummaryResponse> Handle(
        GetMyWithdrawalQuery request,
        CancellationToken cancellationToken)
    {
        var withdrawal = await (
            from item in context.WithdrawalRequests.AsNoTracking()
            join wallet in context.Wallets.AsNoTracking() on item.WalletId equals wallet.Id
            where item.Id == request.WithdrawalId && wallet.UserId == request.UserId
            select item
        ).SingleOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("WITHDRAWAL_NOT_FOUND", nameof(WithdrawalRequest), request.WithdrawalId);

        return WithdrawalReadMapper.ToSummary(withdrawal);
    }
}
