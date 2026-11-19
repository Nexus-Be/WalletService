namespace Wallet.Application.Features.Wallets.Commands.UnfreezeWallet;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Interfaces;

internal sealed class UnfreezeWalletCommandHandler(IWalletDbContext context)
    : IRequestHandler<UnfreezeWalletCommand, WalletFreezeStatusResponse>
{
    public Task<WalletFreezeStatusResponse> Handle(
        UnfreezeWalletCommand request,
        CancellationToken cancellationToken) =>
        WalletFreezeHelper.UpdateFreezeStateAsync(
            context,
            request.UserId,
            freeze: false,
            request.Reason,
            defaultReason: "DISPUTE_RESOLVED",
            request.ActorUserId,
            cancellationToken);
}
