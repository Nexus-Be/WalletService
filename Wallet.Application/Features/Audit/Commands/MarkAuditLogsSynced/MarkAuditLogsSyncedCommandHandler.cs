namespace Wallet.Application.Features.Audit.Commands.MarkAuditLogsSynced;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;

internal sealed class MarkAuditLogsSyncedCommandHandler : IRequestHandler<MarkAuditLogsSyncedCommand, MarkAuditLogsSyncedResult>
{
    private const int MaxIds = 500;
    private readonly IWalletDbContext _context;

    public MarkAuditLogsSyncedCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<MarkAuditLogsSyncedResult> Handle(MarkAuditLogsSyncedCommand request, CancellationToken cancellationToken)
    {
        var ids = (request.Ids ?? Array.Empty<Guid>()).Distinct().Take(MaxIds).ToList();
        if (ids.Count == 0)
        {
            return new MarkAuditLogsSyncedResult(0);
        }

        var rows = await _context.AuditLogs.Where(log => ids.Contains(log.Id) && !log.IsSynced).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsSynced = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new MarkAuditLogsSyncedResult(rows.Count);
    }
}
