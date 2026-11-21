namespace Wallet.Application.Features.Audit.Queries.GetUnsyncedAuditLogs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;

internal sealed class GetUnsyncedAuditLogsQueryHandler : IRequestHandler<GetUnsyncedAuditLogsQuery, UnsyncedAuditLogsResult>
{
    private const int MaxLimit = 500;
    private readonly IWalletDbContext _context;

    public GetUnsyncedAuditLogsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<UnsyncedAuditLogsResult> Handle(GetUnsyncedAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit is > 0 and <= MaxLimit ? request.Limit : 200;
        var rows = await _context.AuditLogs
            .AsNoTracking()
            .Where(log => !log.IsSynced)
            .OrderBy(log => log.CreatedAt)
            .ThenBy(log => log.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var unsynced = await _context.AuditLogs.CountAsync(log => !log.IsSynced, cancellationToken);

        var items = rows.Select(log => new AuditSyncItem(
            log.Id, log.ServiceName, log.TraceId, log.UserId, log.UserName, log.Action.ToString(), log.EntityName, log.EntityId,
            log.OldValues, log.NewValues, log.ChangedColumns, log.IpAddress, log.UserAgent, log.CreatedAt)).ToList();
        return new UnsyncedAuditLogsResult(items, unsynced);
    }
}
