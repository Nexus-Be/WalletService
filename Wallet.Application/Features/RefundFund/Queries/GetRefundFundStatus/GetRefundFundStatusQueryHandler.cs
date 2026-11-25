namespace Wallet.Application.Features.RefundFund.Queries.GetRefundFundStatus;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using global::Wallet.Application.Common;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;

internal sealed class GetRefundFundStatusQueryHandler : IRequestHandler<GetRefundFundStatusQuery, RefundFundStatusResult>
{
    private readonly IWalletDbContext _context;
    private readonly IOptions<RefundFundOptions> _options;

    public GetRefundFundStatusQueryHandler(IWalletDbContext context, IOptions<RefundFundOptions> options)
    {
        _context = context;
        _options = options;
    }

    public async Task<RefundFundStatusResult> Handle(GetRefundFundStatusQuery request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var balance = await _context.Wallets.AsNoTracking().Where(wallet => wallet.UserId == RefundFundAccount.UserId)
            .Select(wallet => (decimal?)wallet.Balance).FirstOrDefaultAsync(cancellationToken) ?? 0m;
        var dayStart = RefundFundClock.StartOfDayUtc(DateTimeOffset.UtcNow);
        var paidToday = await _context.RefundFundPayouts.AsNoTracking().Where(item => item.CreatedAt >= dayStart)
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        return new RefundFundStatusResult(options.Enabled, balance, options.PerCaseCap, options.DailyCap, options.SecondApprovalThreshold, paidToday,
            Math.Max(0m, options.DailyCap - paidToday));
    }
}
