namespace Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Features.Withdrawals;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class CreateBankAccountCommandHandler(IWalletDbContext context)
    : IRequestHandler<CreateBankAccountCommand, BankAccountResponse>
{
    public async Task<BankAccountResponse> Handle(
        CreateBankAccountCommand request,
        CancellationToken cancellationToken)
    {
        var bankError = WithdrawalWorkflow.ValidateManualBankDetails(
            request.BankName,
            request.AccountNumber,
            request.AccountHolderName,
            nameof(CreateBankAccountCommand.BankName),
            "Bank name").FirstOrDefault();
        if (bankError is not null)
        {
            throw new BusinessRuleValidationException(
                "VAL_INVALID_INPUT",
                bankError.ErrorMessage ?? "Bank account details are invalid.");
        }

        var userId = request.UserId.ToString();
        var bankName = request.BankName.Trim();
        var accountNumber = request.AccountNumber.Trim();
        var accountHolderName = request.AccountHolderName.Trim();
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockAuctionKeyAsync($"bank-account:{userId}", cancellationToken);

        var existingAccounts = await context.BankAccounts
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);

        var duplicateExists = existingAccounts.Any(item =>
            string.Equals(item.BankName.Trim(), bankName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.AccountNumber.Trim(), accountNumber, StringComparison.Ordinal));
        if (duplicateExists)
        {
            throw new BusinessRuleValidationException(
                "BANK_ACCOUNT_DUPLICATE",
                "This bank account is already saved for the user.",
                409);
        }

        var makeDefault = request.IsDefault || existingAccounts.Count == 0;
        if (makeDefault)
        {
            var previousDefaults = existingAccounts.Where(item => item.IsDefault).ToList();
            if (previousDefaults.Count > 0)
            {
                foreach (var account in previousDefaults)
                {
                    account.IsDefault = false;
                    account.UpdatedAt = now;
                }

                // Flush IsDefault = false first so inserting a new default row never transiently violates IX_BankAccounts_UserId_Default.
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        var created = new BankAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BankName = bankName,
            AccountNumber = accountNumber,
            AccountHolderName = accountHolderName,
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
