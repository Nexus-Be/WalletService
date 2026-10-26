namespace Wallet.Application.Features.Queries.GetWallet;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Application.DTOs;
using Wallet.Domain.Exceptions;

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
        if (!Guid.TryParse(request.UserId, out var userId) || userId == Guid.Empty)
            throw new ArgumentException("UserId must be a valid UUID.", nameof(request.UserId));

        var wallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet == null)
            throw new WalletNotFoundException(request.UserId);

        return new WalletDto
        {
            Id = wallet.Id.ToString(),
            UserId = wallet.UserId.ToString(),
            Balance = wallet.Balance,
            LockedBalance = wallet.LockedBalance,
            AvailableBalance = wallet.AvailableBalance,
            Currency = wallet.Currency.ToString(),
            Status = wallet.Status,
            UpdatedAt = wallet.UpdatedAt
        };
    }
}