namespace Wallet.Application.Features.Wallets;

using System;
using System.Threading;
using System.Threading.Tasks;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using global::Wallet.Domain.Entities;
using global::Wallet.Domain.Enums;
using global::Wallet.Domain.Exceptions;

public sealed record WalletFreezeStatusResponse(
    Guid WalletId,
    Guid UserId,
    string Status,
    string Reason,
    DateTimeOffset UpdatedAt);

internal static class WalletFreezeHelper
{
    internal static async Task<WalletFreezeStatusResponse> UpdateFreezeStateAsync(
        IWalletDbContext context,
        Guid userId,
        bool freeze,
        string? reason,
        string defaultReason,
        string? actorUserId,
        CancellationToken cancellationToken)
    {
        var resolvedReason = string.IsNullOrWhiteSpace(reason) ? defaultReason : reason.Trim();
        var targetStatus = freeze ? "FROZEN" : "ACTIVE";

        if (freeze && userId == RefundFundAccount.UserId)
        {
            throw new BusinessRuleValidationException(
                "SYSTEM_WALLET_PROTECTED",
                "System wallets cannot be frozen.",
                403);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var wallet = freeze
            ? await context.LockOrEnsureWalletByUserIdAsync(userId, cancellationToken)
            : await context.LockWalletByUserIdAsync(userId, cancellationToken)
              ?? throw new WalletNotFoundException(userId.ToString());

        if (!string.Equals(wallet.Status, targetStatus, StringComparison.OrdinalIgnoreCase))
        {
            var oldStatus = wallet.Status;
            if (freeze)
            {
                wallet.Freeze();
            }
            else
            {
                wallet.Unfreeze();
            }

            context.AddAuditLog(
                action: freeze ? AuditAction.WalletFrozen : AuditAction.WalletUnfrozen,
                entityName: nameof(WalletAccount),
                entityId: wallet.Id.ToString(),
                oldValues: new { Status = oldStatus },
                newValues: new { wallet.Status, Reason = resolvedReason },
                userId: string.IsNullOrWhiteSpace(actorUserId) ? userId.ToString() : actorUserId);

            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new WalletFreezeStatusResponse(
            wallet.Id,
            wallet.UserId,
            wallet.Status.ToApiStatus(),
            resolvedReason,
            wallet.UpdatedAt);
    }
}
