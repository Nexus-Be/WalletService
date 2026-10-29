namespace Wallet.Application.Consumers.User;

using System;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Events;

public sealed class UserRegisteredConsumer : IConsumer<IUserRegisteredEvent>
{
    private readonly IWalletDbContext _context;

    public UserRegisteredConsumer(IWalletDbContext context)
    {
        _context = context;
    }

    public async Task Consume(ConsumeContext<IUserRegisteredEvent> context)
    {
        if (!Guid.TryParse(context.Message.UserId, out var userId) || userId == Guid.Empty)
            throw new InvalidOperationException("UserRegistered event has an invalid UserId.");

        if (await _context.Wallets.AnyAsync(w => w.UserId == userId, context.CancellationToken))
            return;

        _context.Wallets.Add(new WalletAccount
        {
            UserId = userId,
            Balance = 0m,
            LockedBalance = 0m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        });
        await _context.SaveChangesAsync(context.CancellationToken);
    }
}
