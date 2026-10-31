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
            .FirstOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);

        if (wallet is null)
        {
            wallet = new WalletAccount
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE",
                Balance = 0m,
                LockedBalance = 0m
            };
            _context.Wallets.Add(wallet);
            _context.AddAuditLog(
                action: AuditAction.WalletCreate,
                entityName: nameof(WalletAccount),
                entityId: wallet.Id.ToString(),
                newValues: new { wallet.UserId, wallet.Balance, wallet.LockedBalance, wallet.Currency },
                userId: request.UserId.ToString());

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _context.Wallets.Remove(wallet);
                wallet = await _context.Wallets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.UserId == request.UserId, cancellationToken);

                if (wallet is null)
                {
                    throw;
                }
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
