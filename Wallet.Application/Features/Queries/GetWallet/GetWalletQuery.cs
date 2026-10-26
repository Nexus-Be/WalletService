namespace Wallet.Application.Features.Queries.GetWallet;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;

public record GetWalletQuery(string UserId) : IRequest<WalletDto>;

public class GetWalletQueryHandler : IRequestHandler<GetWalletQuery, WalletDto>
{
    private readonly IWalletDbContext _context;

    public GetWalletQueryHandler(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task<WalletDto> Handle(GetWalletQuery request, CancellationToken cancellationToken)
    {
        _ = Guid.TryParse(request.UserId, out var uid);

        var wallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == uid, cancellationToken);

        if (wallet == null)
        {
            // Auto create wallet for user if first time accessing
            var newWallet = new WalletAccount
            {
                UserId = uid != Guid.Empty ? uid : Guid.NewGuid(),
                Balance = 0m,
                Currency = CurrencyCode.Vnd,
                Status = "ACTIVE"
            };

            _context.Wallets.Add(newWallet);
            await _context.SaveChangesAsync(cancellationToken);
            wallet = newWallet;
        }

        return new WalletDto
        {
            Id = wallet.Id.ToString(),
            UserId = wallet.UserId.ToString(),
            Balance = wallet.Balance,
            Currency = wallet.Currency.ToString(),
            Status = wallet.Status,
            UpdatedAt = wallet.UpdatedAt
        };
    }
}
