namespace Wallet.Application.Features.BankAccounts.Commands.SetDefaultBankAccount;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class SetDefaultBankAccountCommandHandler(IWalletDbContext context)
    : IRequestHandler<SetDefaultBankAccountCommand, BankAccountResponse>
{
    public async Task<BankAccountResponse> Handle(
        SetDefaultBankAccountCommand request,
        CancellationToken cancellationToken)
    {
        var userId = request.UserId.ToString();
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockAuctionKeyAsync($"bank-account:{userId}", cancellationToken);

        var accounts = await context.BankAccounts
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);

        var target = accounts.SingleOrDefault(item => item.Id == request.BankAccountId)
            ?? throw new EntityNotFoundException("BANK_ACCOUNT_NOT_FOUND", nameof(BankAccount), request.BankAccountId);

        if (!target.IsDefault)
        {
            var previousDefaults = accounts.Where(item => item.Id != target.Id && item.IsDefault).ToList();
            if (previousDefaults.Count > 0)
            {
                foreach (var account in previousDefaults)
                {
                    account.IsDefault = false;
                    account.UpdatedAt = now;
                }

                // Flush IsDefault = false first so UUID-ordered updates never transiently violate IX_BankAccounts_UserId_Default.
                await context.SaveChangesAsync(cancellationToken);
            }

            target.IsDefault = true;
            target.UpdatedAt = now;

            context.AddAuditLog(
                AuditAction.BankAccountChange,
                nameof(BankAccount),
                target.Id.ToString(),
                oldValues: new { IsDefault = false },
                newValues: new { IsDefault = true },
                userId: userId);

            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return BankAccountResponse.FromEntity(target);
    }
}
