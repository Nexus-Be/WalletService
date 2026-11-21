namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Wallet.Application.Features.BankAccounts.Commands.SetDefaultBankAccount;
using Wallet.Application.Features.BankAccounts.Queries.ListMyBankAccounts;
using Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;
using Wallet.Tests.Support;
using Xunit;

public sealed class BankAccountTests
{
    [Fact]
    public async Task BankAccountLifecycle_CreateSetDefaultDeletePromotesNextDefault()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        var create = new CreateBankAccountCommandHandler(context);
        var setDefault = new SetDefaultBankAccountCommandHandler(context);
        var delete = new DeleteBankAccountCommandHandler(context);
        var list = new ListMyBankAccountsQueryHandler(context);

        var first = await create.Handle(
            new CreateBankAccountCommand("VCB", "1111222233", "Seller One", IsDefault: false) { UserId = userId },
            default);
        Assert.True(first.IsDefault);

        var second = await create.Handle(
            new CreateBankAccountCommand("ACB", "4444555566", "Seller One", IsDefault: true) { UserId = userId },
            default);
        Assert.True(second.IsDefault);

        var allAfterSecond = await list.Handle(new ListMyBankAccountsQuery { UserId = userId }, default);
        Assert.Equal(2, allAfterSecond.Count);
        Assert.Single(allAfterSecond, item => item.IsDefault && item.Id == second.Id);

        await setDefault.Handle(new SetDefaultBankAccountCommand(first.Id) { UserId = userId }, default);
        var allAfterFlip = await list.Handle(new ListMyBankAccountsQuery { UserId = userId }, default);
        Assert.Single(allAfterFlip, item => item.IsDefault && item.Id == first.Id);

        await delete.Handle(new DeleteBankAccountCommand(first.Id) { UserId = userId }, default);
        var remaining = await list.Handle(new ListMyBankAccountsQuery { UserId = userId }, default);
        var promoted = Assert.Single(remaining);
        Assert.Equal(second.Id, promoted.Id);
        Assert.True(promoted.IsDefault);
    }

    [Fact]
    public async Task DeleteBankAccount_InUseByPendingWithdrawal_Returns409()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        context.Wallets.Add(new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = 500_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        });
        await context.SaveChangesAsync();

        var saved = await new CreateBankAccountCommandHandler(context).Handle(
            new CreateBankAccountCommand("TPB", "7777888899", "Seller One", IsDefault: true) { UserId = userId },
            default);

        await new RequestWithdrawalCommandHandler(context).Handle(
            new RequestWithdrawalCommand(100_000m, BankAccountId: saved.Id)
            {
                UserId = userId.ToString(),
                IdempotencyKey = "wd-using-saved"
            },
            default);

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new DeleteBankAccountCommandHandler(context).Handle(
                new DeleteBankAccountCommand(saved.Id) { UserId = userId },
                default));

        Assert.Equal("BANK_ACCOUNT_IN_USE", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task RequestWithdrawal_FallsBackToDefaultBankAccount_AndRejectsWhenMissing()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        context.Wallets.Add(new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = 500_000m,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE"
        });
        await context.SaveChangesAsync();

        var reqHandler = new RequestWithdrawalCommandHandler(context);
        var missingErr = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            reqHandler.Handle(
                new RequestWithdrawalCommand(100_000m) { UserId = userId.ToString(), IdempotencyKey = "no-default" },
                default));
        Assert.Equal("BANK_ACCOUNT_REQUIRED", missingErr.ErrorCode);

        var createdAccount = await new CreateBankAccountCommandHandler(context).Handle(
            new CreateBankAccountCommand("MB", "9999000011", "Seller One", IsDefault: true) { UserId = userId },
            default);

        var withdrawal = await reqHandler.Handle(
            new RequestWithdrawalCommand(100_000m) { UserId = userId.ToString(), IdempotencyKey = "use-default" },
            default);

        var entity = context.WithdrawalRequests.Single(r => r.Id == Guid.Parse(withdrawal.WithdrawalId));
        Assert.Equal(createdAccount.Id, entity.BankAccountId);
        Assert.Equal("MB", entity.BankName);
        Assert.Equal("9999000011", entity.BankAccountNumber);
        Assert.Equal("Seller One", entity.BankAccountHolder);
    }

    [Fact]
    public async Task SetDefaultAndDelete_OtherUserAccount_Returns404()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        var created = await new CreateBankAccountCommandHandler(context).Handle(
            new CreateBankAccountCommand("VCB", "1111222233", "Owner", IsDefault: true) { UserId = ownerId },
            default);

        var setDefaultErr = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            new SetDefaultBankAccountCommandHandler(context).Handle(
                new SetDefaultBankAccountCommand(created.Id) { UserId = strangerId },
                default));
        Assert.Equal("BANK_ACCOUNT_NOT_FOUND", setDefaultErr.ErrorCode);

        var deleteErr = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            new DeleteBankAccountCommandHandler(context).Handle(
                new DeleteBankAccountCommand(created.Id) { UserId = strangerId },
                default));
        Assert.Equal("BANK_ACCOUNT_NOT_FOUND", deleteErr.ErrorCode);
    }

    [Theory]
    [InlineData(WithdrawalStatus.Approved)]
    [InlineData(WithdrawalStatus.Processing)]
    public async Task DeleteBankAccount_ActiveWithdrawalStatus_BlocksDeletionWith409(WithdrawalStatus status)
    {
        using var context = new TestWalletDbContext(nameof(DeleteBankAccount_ActiveWithdrawalStatus_BlocksDeletionWith409) + status);
        var userId = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = userId, Balance = 500_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();

        var account = await new CreateBankAccountCommandHandler(context).Handle(
            new CreateBankAccountCommand("ACB", "2222333344", "Seller", IsDefault: true) { UserId = userId },
            default);

        context.WithdrawalRequests.Add(new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            BankAccountId = account.Id,
            Amount = 100_000m,
            NetAmount = 100_000m,
            BankName = "ACB",
            BankAccountNumber = "2222333344",
            BankAccountHolder = "Seller",
            Status = status
        });
        await context.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<BusinessRuleValidationException>(() =>
            new DeleteBankAccountCommandHandler(context).Handle(
                new DeleteBankAccountCommand(account.Id) { UserId = userId },
                default));

        Assert.Equal("BANK_ACCOUNT_IN_USE", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Theory]
    [InlineData(WithdrawalStatus.Completed)]
    [InlineData(WithdrawalStatus.Rejected)]
    [InlineData(WithdrawalStatus.Failed)]
    [InlineData(WithdrawalStatus.Cancelled)]
    public async Task DeleteBankAccount_TerminalWithdrawalStatus_AllowsDeletion(WithdrawalStatus status)
    {
        using var context = new TestWalletDbContext(nameof(DeleteBankAccount_TerminalWithdrawalStatus_AllowsDeletion) + status);
        var userId = Guid.NewGuid();
        var wallet = new WalletAccount { Id = Guid.NewGuid(), UserId = userId, Balance = 500_000m, Currency = CurrencyCode.Vnd, Status = "ACTIVE" };
        context.Wallets.Add(wallet);
        await context.SaveChangesAsync();

        var account = await new CreateBankAccountCommandHandler(context).Handle(
            new CreateBankAccountCommand("TPB", "3333444455", "Seller", IsDefault: true) { UserId = userId },
            default);

        context.WithdrawalRequests.Add(new WithdrawalRequest
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            BankAccountId = account.Id,
            Amount = 100_000m,
            NetAmount = 100_000m,
            BankName = "TPB",
            BankAccountNumber = "3333444455",
            BankAccountHolder = "Seller",
            Status = status
        });
        await context.SaveChangesAsync();

        await new DeleteBankAccountCommandHandler(context).Handle(
            new DeleteBankAccountCommand(account.Id) { UserId = userId },
            default);

        Assert.Empty(context.BankAccounts);
    }

    [Fact]
    public async Task DeleteNonDefaultAndLast_AndSetDefaultIdempotentReplay_MaintainInvariants()
    {
        using var context = new TestWalletDbContext(Guid.NewGuid().ToString());
        var userId = Guid.NewGuid();
        var create = new CreateBankAccountCommandHandler(context);
        var setDefault = new SetDefaultBankAccountCommandHandler(context);
        var delete = new DeleteBankAccountCommandHandler(context);

        var first = await create.Handle(
            new CreateBankAccountCommand("VCB", "4444555566", "Seller", IsDefault: true) { UserId = userId },
            default);
        var second = await create.Handle(
            new CreateBankAccountCommand("MB", "5555666677", "Seller", IsDefault: false) { UserId = userId },
            default);

        // Idempotent replay when target is already default avoids redundant audit log entries.
        var auditsBefore = context.AuditLogs.Count();
        var replayed = await setDefault.Handle(new SetDefaultBankAccountCommand(first.Id) { UserId = userId }, default);
        Assert.True(replayed.IsDefault);
        Assert.Equal(auditsBefore, context.AuditLogs.Count());

        // Deleting non-default leaves first as default.
        await delete.Handle(new DeleteBankAccountCommand(second.Id) { UserId = userId }, default);
        var remaining = Assert.Single(context.BankAccounts);
        Assert.Equal(first.Id, remaining.Id);
        Assert.True(remaining.IsDefault);

        // Deleting the last account succeeds cleanly with zero remaining active accounts.
        await delete.Handle(new DeleteBankAccountCommand(first.Id) { UserId = userId }, default);
        Assert.Empty(context.BankAccounts);
    }

    [Fact]
    public void BankAccountCommands_RequestValidation_RejectsInvalidInputs()
    {
        var invalidCreate = new CreateBankAccountCommand("A", "12", "X") { UserId = Guid.NewGuid() };
        Assert.False(RequestValidation.TryValidate(invalidCreate, new List<ValidationResult>()));

        var validCreate = new CreateBankAccountCommand("VCB", "1234567890", "Seller") { UserId = Guid.NewGuid() };
        Assert.True(RequestValidation.TryValidate(validCreate, new List<ValidationResult>()));

        var invalidDefault = new SetDefaultBankAccountCommand(Guid.Empty) { UserId = Guid.NewGuid() };
        Assert.False(RequestValidation.TryValidate(invalidDefault, new List<ValidationResult>()));

        var invalidDelete = new DeleteBankAccountCommand(Guid.Empty) { UserId = Guid.NewGuid() };
        Assert.False(RequestValidation.TryValidate(invalidDelete, new List<ValidationResult>()));
    }
}
