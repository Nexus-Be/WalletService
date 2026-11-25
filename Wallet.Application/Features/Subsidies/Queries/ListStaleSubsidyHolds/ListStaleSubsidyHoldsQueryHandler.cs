namespace Wallet.Application.Features.Subsidies.Queries.ListStaleSubsidyHolds;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Domain.Enums;
using Wallet.Application.Interfaces;

internal sealed class ListStaleSubsidyHoldsQueryHandler : IRequestHandler<ListStaleSubsidyHoldsQuery, IReadOnlyList<StaleSubsidyHoldResult>>
{
    private readonly IWalletDbContext _context;

    public ListStaleSubsidyHoldsQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<StaleSubsidyHoldResult>> Handle(ListStaleSubsidyHoldsQuery request, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-request.OlderThanMinutes);
        return await _context.PlatformSubsidyReservations.AsNoTracking()
            .Where(item => item.Status == SubsidyReservationStatus.Held && item.CreatedAt <= cutoff)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Take(request.Limit)
            .Select(item => new StaleSubsidyHoldResult(item.SubOrderId, item.OrderId, item.Amount, item.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
