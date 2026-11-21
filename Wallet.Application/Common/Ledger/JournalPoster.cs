namespace Wallet.Application.Common.Ledger;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

/// <summary>Posts waiting ledger events as journal entries, one database transaction per event. A failure is recorded and retried; it never touches the money.</summary>
public sealed class JournalPoster
{
    public const int MaxAttempts = 5;
    private readonly IWalletDbContext _context;

    public JournalPoster(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task EnsureAccountsAsync(CancellationToken cancellationToken)
    {
        var existing = await _context.LedgerAccounts.Select(account => account.Code).ToListAsync(cancellationToken);
        var missing = LedgerPostingRules.Accounts.Where(pair => !existing.Contains(pair.Key)).ToList();
        foreach (var (code, name) in missing)
        {
            _context.LedgerAccounts.Add(new LedgerAccount { Code = code, Name = name });
        }

        if (missing.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Posts up to <paramref name="batchSize"/> events, oldest first. Returns how many were posted.</summary>
    public async Task<int> PostPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var rows = await _context.LedgerPostingPending
            .Where(row => row.Status == LedgerPostingStatus.Pending
                || (row.Status == LedgerPostingStatus.Failed && row.Attempts < MaxAttempts))
            .OrderBy(row => row.WalletId).ThenBy(row => row.LedgerEpoch).ThenBy(row => row.LedgerSeq)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var posted = 0;
        foreach (var row in rows)
        {
            try
            {
                var exists = await _context.JournalEntries.AnyAsync(entry => entry.PendingId == row.Id, cancellationToken);
                if (!exists)
                {
                    var entry = LedgerPostingRules.BuildEntry(row)!;
                    _context.JournalEntries.Add(entry);
                }

                row.Status = LedgerPostingStatus.Posted;
                row.PostedAt = DateTimeOffset.UtcNow;
                row.LastError = null;
                await _context.SaveChangesAsync(cancellationToken);
                posted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Forget the half-built entry, then record the failure so the row is tried again later.
                foreach (var stale in _context.JournalEntries.Local.Where(entry => entry.PendingId == row.Id).ToList())
                {
                    _context.JournalEntries.Remove(stale);
                }

                row.Status = LedgerPostingStatus.Failed;
                row.Attempts++;
                row.LastError = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message;
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    // The failure itself cannot be recorded right now; the row stays Pending and is picked up again.
                }
            }
        }

        return posted;
    }
}
