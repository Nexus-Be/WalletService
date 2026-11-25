namespace Wallet.Application.Features.Ledger.Queries.GetLedgerStatus;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Ledger;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class GetLedgerStatusQueryHandler : IRequestHandler<GetLedgerStatusQuery, LedgerStatusResult>
{
    private readonly IWalletDbContext _context;
    private readonly IReconciliationSourceClient _sources;

    public GetLedgerStatusQueryHandler(IWalletDbContext context, IReconciliationSourceClient sources)
    {
        _context = context;
        _sources = sources;
    }

    public async Task<LedgerStatusResult> Handle(GetLedgerStatusQuery request, CancellationToken cancellationToken)
    {
        var settings = await _context.LedgerSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? new LedgerSettings();

        var evaluator = new ReconciliationEvaluator(_context, _sources);
        var results = (await evaluator.EvaluateWalletLedgersAsync(cancellationToken)).ToDictionary(item => item.WalletId);

        var now = DateTimeOffset.UtcNow;
        var queue = await _context.LedgerPostingPending.AsNoTracking()
            .Where(row => row.Status != LedgerPostingStatus.Posted)
            .Select(row => new { row.WalletId, row.Status, row.CreatedAt }).ToListAsync(cancellationToken);
        var covered = (await _context.LedgerPostingPending.AsNoTracking().Where(row => row.RelatedTransactionIds != null)
            .Select(row => row.RelatedTransactionIds!).ToListAsync(cancellationToken))
            .SelectMany(text => text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty).ToHashSet();
        var openings = (await _context.LedgerPostingPending.AsNoTracking().Where(row => row.IsEpochOpening)
            .GroupBy(row => row.WalletId).Select(group => new { group.Key, At = group.Min(row => row.CreatedAt) }).ToListAsync(cancellationToken))
            .ToDictionary(item => item.Key, item => item.At);

        var wallets = new List<LedgerWalletStatus>();
        foreach (var walletId in results.Keys.OrderBy(id => id))
        {
            var wallet = await _context.Wallets.AsNoTracking().FirstAsync(item => item.Id == walletId, cancellationToken);
            var opening = openings.TryGetValue(walletId, out var at) ? at : DateTimeOffset.MaxValue;
            var historic = await _context.WalletTransactions.AsNoTracking()
                .Where(tx => tx.WalletId == walletId && tx.Status == TransactionStatus.Success && tx.Type != WalletTransactionType.DirectGatewayPayment
                    && tx.BalanceBefore != tx.BalanceAfter && tx.CreatedAt < opening)
                .Select(tx => tx.Id).ToListAsync(cancellationToken);
            var missing = historic.Count(id => !covered.Contains(id));
            var mine = queue.Where(row => row.WalletId == walletId).ToList();
            var result = results[walletId];
            wallets.Add(new LedgerWalletStatus(
                walletId, wallet.LedgerEpoch, missing, missing == 0 ? "Complete" : "Incomplete",
                mine.Count(row => row.Status == LedgerPostingStatus.Pending), mine.Count(row => row.Status == LedgerPostingStatus.Failed),
                mine.Count == 0 ? null : (long)(now - mine.Min(row => row.CreatedAt)).TotalSeconds,
                result.Result, result.Reason));
        }

        return new LedgerStatusResult(
            settings.Enabled, settings.ShadowEpoch, settings.Version, settings.UpdatedAt,
            wallets.Count(item => item.I10Result == "Matched"), wallets.Count(item => item.I10Result == "Mismatched"), wallets.Count(item => item.I10Result == "Unchecked"),
            wallets);
    }
}
