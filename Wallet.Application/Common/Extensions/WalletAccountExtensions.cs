namespace Wallet.Application.Common.Extensions;

using System;
using System.Threading;
using System.Threading.Tasks;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal static class WalletAccountExtensions
{
    internal static WalletAccount StageDefaultWallet(this IWalletDbContext context, Guid userId)
    {
        var wallet = new WalletAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Currency = CurrencyCode.Vnd,
            Status = "ACTIVE",
            Balance = 0m,
            LockedBalance = 0m
        };
        context.Wallets.Add(wallet);
        context.AddAuditLog(
            action: AuditAction.WalletCreate,
            entityName: nameof(WalletAccount),
            entityId: wallet.Id.ToString(),
            newValues: new { wallet.UserId, wallet.Balance, wallet.LockedBalance, wallet.Currency },
            userId: userId.ToString());
        return wallet;
    }

    internal static async Task<WalletAccount> LockOrEnsureWalletByUserIdAsync(
        this IWalletDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var wallet = await context.LockWalletByUserIdAsync(userId, cancellationToken);
        if (wallet is not null)
        {
            return wallet;
        }

        wallet = context.StageDefaultWallet(userId);
        await context.SaveChangesAsync(cancellationToken);
        return wallet;
    }

    internal static void EnsureActive(this WalletAccount wallet)
    {
        if (!string.Equals(wallet.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleValidationException("WALLET_FROZEN", "The wallet is not active.", 403);
        }
    }
}
