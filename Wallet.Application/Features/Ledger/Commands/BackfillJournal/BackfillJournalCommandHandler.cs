namespace Wallet.Application.Features.Ledger.Commands.BackfillJournal;

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

using Wallet.Application.Common.Extensions;

internal sealed class BackfillJournalCommandHandler : IRequestHandler<BackfillJournalCommand, BackfillJournalResult>
{
    private readonly IWalletDbContext _context;

    public BackfillJournalCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Writes journal entries for balance changes that happened before the ledger followed a wallet, but only for wallets whose history
    /// is a provable chain (each transaction starts from the balance the previous one ended with). Other wallets are skipped and stay
    /// "unchecked" for I10(b). Safe to run again: a transaction that already has an entry is not entered twice.
    /// </summary>
    public async Task<BackfillJournalResult> Handle(BackfillJournalCommand request, CancellationToken cancellationToken)
    {
        var wallets = _context.Wallets.AsNoTracking().AsQueryable();
        if (request.WalletId.HasValue) wallets = wallets.Where(wallet => wallet.Id == request.WalletId.Value);
        var ids = await wallets.Select(wallet => wallet.Id).ToListAsync(cancellationToken);

        int done = 0, skipped = 0, entries = 0;
        foreach (var walletId in ids)
        {
            var chain = await _context.WalletTransactions.AsNoTracking()
                .Where(tx => tx.WalletId == walletId && tx.Status == TransactionStatus.Success && tx.Type != WalletTransactionType.DirectGatewayPayment && tx.BalanceBefore != tx.BalanceAfter)
                .OrderBy(tx => tx.CreatedAt).ThenBy(tx => tx.Id).ToListAsync(cancellationToken);
            if (chain.Count == 0) continue;

            var provable = chain[0].BalanceBefore == 0m;
            for (var i = 1; provable && i < chain.Count; i++)
                provable = chain[i].BalanceBefore == chain[i - 1].BalanceAfter;
            if (!provable)
            {
                skipped++;
                continue;
            }

            var covered = (await _context.LedgerPostingPending.AsNoTracking().Where(row => row.WalletId == walletId && row.RelatedTransactionIds != null)
                .Select(row => row.RelatedTransactionIds!).ToListAsync(cancellationToken))
                .SelectMany(text => text.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
            var entered = (await _context.JournalEntries.AsNoTracking().Where(entry => entry.WalletId == walletId && entry.Source == "Backfill" && entry.WalletTransactionId != null)
                .Select(entry => entry.WalletTransactionId!.Value).ToListAsync(cancellationToken)).ToHashSet();

            foreach (var tx in chain.Where(tx => !covered.Contains(tx.Id.ToString()) && !entered.Contains(tx.Id)))
            {
                var pending = new LedgerPostingPending { WalletId = walletId, Type = tx.Type.ToString(), Delta = tx.BalanceAfter - tx.BalanceBefore, WalletTransactionId = tx.Id };
                var entry = LedgerPostingRules.BuildEntry(pending)!;
                entry.PendingId = null;
                entry.Source = "Backfill";
                entry.CreatedAt = tx.CreatedAt;
                _context.JournalEntries.Add(entry);
                entries++;
            }

            done++;
        }

        if (entries > 0)
        {
            _context.AddAuditLog(
                AuditAction.LedgerBackfill, nameof(JournalEntry), "backfill",
                newValues: new { done, skipped, entries }, userId: request.AdminUserId.ToString());
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new BackfillJournalResult(done, skipped, entries);
    }
}
