namespace Wallet.Application.Features.Wallets.Commands.FreezeWallet;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Wallet.Application.Interfaces;

internal sealed class FreezeWalletCommandHandler(IWalletDbContext context)
    : IRequestHandler<FreezeWalletCommand, WalletFreezeStatusResponse>
{
    public Task<WalletFreezeStatusResponse> Handle(
        FreezeWalletCommand request,
        CancellationToken cancellationToken) =>
        WalletFreezeHelper.UpdateFreezeStateAsync(
            context,
            request.UserId,
            freeze: true,
            request.Reason,
            defaultReason: "DISPUTE_FREEZE",
            request.ActorUserId,
            cancellationToken);
}
