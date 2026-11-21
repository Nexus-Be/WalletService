namespace Wallet.Application.Features.Wallets.Commands.EnsureWallet;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Features.Wallets.Queries.GetMyWallet;
using global::Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;

internal sealed class EnsureWalletCommandHandler : IRequestHandler<EnsureWalletCommand, WalletResponse>
{
    private readonly IWalletDbContext _context;

    public EnsureWalletCommandHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletResponse> Handle(EnsureWalletCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new ArgumentException("User ID must be a valid UUID.", nameof(request));
        }

        var wallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);

        if (wallet is null)
        {
            var ownsTransaction = _context.Database.CurrentTransaction is null;
            await using var tx = ownsTransaction
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await _context.LockAuctionKeyAsync($"wallet-create:{request.UserId:D}", cancellationToken);

            wallet = await _context.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);

            if (wallet is null)
            {
                var (stagedWallet, stagedAudit) = _context.StageDefaultWalletWithAudit(request.UserId);

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    if (tx is not null)
                    {
                        await tx.CommitAsync(cancellationToken);
                    }

                    wallet = stagedWallet;
                }
                catch (DbUpdateException)
                {
                    _context.Wallets.Remove(stagedWallet);
                    _context.AuditLogs.Remove(stagedAudit);
                    if (tx is not null)
                    {
                        await tx.RollbackAsync(cancellationToken);
                    }

                    wallet = await _context.Wallets
                        .AsNoTracking()
                        .FirstOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);

                    if (wallet is null)
                    {
                        throw;
                    }
                }
            }
            else if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        return new WalletResponse(
            wallet.Id.ToString(),
            wallet.UserId.ToString(),
            wallet.Balance,
            wallet.AvailableBalance,
            wallet.LockedBalance,
            wallet.Currency.ToString(),
            wallet.Status.ToApiStatus(),
            wallet.UpdatedAt);
    }
}
