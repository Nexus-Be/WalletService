namespace Wallet.Application.Features.AdminWallets.Queries.GetWithdrawalHistory;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Features.Withdrawals;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Exceptions;

internal sealed class GetWithdrawalHistoryQueryHandler(IWalletDbContext context)
    : IRequestHandler<GetWithdrawalHistoryQuery, IReadOnlyList<WithdrawalHistoryEntry>>
{
    public async Task<IReadOnlyList<WithdrawalHistoryEntry>> Handle(
        GetWithdrawalHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var exists = await context.WithdrawalRequests
            .AsNoTracking()
            .AnyAsync(item => item.Id == request.WithdrawalId, cancellationToken);
        if (!exists)
        {
            throw new EntityNotFoundException("WITHDRAWAL_NOT_FOUND", nameof(WithdrawalRequest), request.WithdrawalId);
        }

        var entityId = request.WithdrawalId.ToString();
        var logs = await context.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityName == nameof(WithdrawalRequest) && log.EntityId == entityId)
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .ToListAsync(cancellationToken);

        return logs
            .Select(log => new WithdrawalHistoryEntry(
                log.Id,
                log.Action.ToString(),
                log.UserId,
                log.OldValues,
                log.NewValues,
                log.CreatedAt))
            .ToArray();
    }
}
