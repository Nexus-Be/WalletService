namespace Wallet.Application.Features.Wallets.Queries.GetMyWallet;

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using global::Wallet.Application.Common.Extensions;
using global::Wallet.Application.Features.Wallets.Commands.EnsureWallet;
using global::Wallet.Application.Interfaces;

internal sealed class GetMyWalletQueryHandler : IRequestHandler<GetMyWalletQuery, WalletResponse>
{
    private readonly IWalletDbContext _context;
    private readonly ISender _sender;

    public GetMyWalletQueryHandler(IWalletDbContext context, ISender sender)
    {
        _context = context;
        _sender = sender;
    }

    public async Task<WalletResponse> Handle(GetMyWalletQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userGuid) || userGuid == Guid.Empty)
            throw new ArgumentException("User ID must be a valid UUID.", nameof(request));

        var wallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userGuid, cancellationToken);

        if (wallet is null)
        {
            return await _sender.Send(new EnsureWalletCommand(userGuid), cancellationToken);
        }

        return new WalletResponse(
            wallet.Id.ToString(),
            wallet.UserId.ToString(),
            wallet.Balance,
            wallet.AvailableBalance,
            wallet.LockedBalance,
            wallet.Currency.ToString(),
            wallet.Status.ToApiStatus(),
            wallet.UpdatedAt
        );
    }
}
