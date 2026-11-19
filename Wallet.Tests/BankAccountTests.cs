namespace Wallet.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
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
}
