namespace Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;

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

internal sealed class CreateBankAccountCommandHandler(IWalletDbContext context)
    : IRequestHandler<CreateBankAccountCommand, BankAccountResponse>
{
    public async Task<BankAccountResponse> Handle(
        CreateBankAccountCommand request,
        CancellationToken cancellationToken)
    {
        var userId = request.UserId.ToString();
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockAuctionKeyAsync($"bank-account:{userId}", cancellationToken);

        var existingAccounts = await context.BankAccounts
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);

        var makeDefault = request.IsDefault || existingAccounts.Count == 0;
        if (makeDefault)
        {
            foreach (var account in existingAccounts.Where(item => item.IsDefault))
            {
                account.IsDefault = false;
                account.UpdatedAt = now;
            }
        }

        var created = new BankAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BankName = request.BankName.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            AccountHolderName = request.AccountHolderName.Trim(),
            IsDefault = makeDefault,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.BankAccounts.Add(created);
        context.AddAuditLog(
            AuditAction.BankAccountChange,
            nameof(BankAccount),
            created.Id.ToString(),
            newValues: new { created.BankName, created.IsDefault },
            userId: userId);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return BankAccountResponse.FromEntity(created);
    }
}
