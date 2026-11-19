namespace Wallet.Application.Features.BankAccounts.Commands.DeleteBankAccount;

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

internal sealed class DeleteBankAccountCommandHandler(IWalletDbContext context)
    : IRequestHandler<DeleteBankAccountCommand, Unit>
{
    public async Task<Unit> Handle(
        DeleteBankAccountCommand request,
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

        var walletId = await context.Wallets
            .AsNoTracking()
            .Where(item => item.UserId == request.UserId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (walletId is Guid wId)
        {
            var inUse = await context.WithdrawalRequests
                .AsNoTracking()
                .AnyAsync(
                    item => item.WalletId == wId &&
                            (item.Status == WithdrawalStatus.Pending ||
                             item.Status == WithdrawalStatus.Approved ||
                             item.Status == WithdrawalStatus.Processing) &&
                            (item.BankAccountId == target.Id ||
                             (item.BankAccountId == null &&
                              item.BankName == target.BankName &&
                              item.BankAccountNumber == target.AccountNumber)),
                    cancellationToken);

            if (inUse)
            {
                throw new BusinessRuleValidationException(
                    "BANK_ACCOUNT_IN_USE",
                    "Cannot delete a bank account referenced by an active withdrawal request.",
                    409);
            }
        }

        var wasDefault = target.IsDefault;
        target.IsDefault = false;
        target.IsDeleted = true;
        target.DeletedAt = now;
        target.UpdatedAt = now;

        Guid? promotedId = null;
        if (wasDefault)
        {
            var next = accounts
                .Where(item => item.Id != target.Id && !item.IsDeleted)
                .OrderByDescending(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .FirstOrDefault();
            if (next is not null)
            {
                // Flush the deleted row's IsDefault = false before promoting the next default against IX_BankAccounts_UserId_Default.
                await context.SaveChangesAsync(cancellationToken);

                next.IsDefault = true;
                next.UpdatedAt = now;
                promotedId = next.Id;
            }
        }

        context.AddAuditLog(
            AuditAction.BankAccountChange,
            nameof(BankAccount),
            target.Id.ToString(),
            oldValues: new { target.BankName, WasDefault = wasDefault },
            newValues: new { Deleted = true, PromotedDefaultId = promotedId },
            userId: userId);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Unit.Value;
    }
}
