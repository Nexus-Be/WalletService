namespace Wallet.Application.Features.Holds.Commands.ReleaseHold;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class ReleaseHoldCommandHandler(IWalletDbContext context)
    : IRequestHandler<ReleaseHoldCommand, ReleaseHoldResponse>
{
    public async Task<ReleaseHoldResponse> Handle(ReleaseHoldCommand request, CancellationToken cancellationToken)
    {
        if (request.ValidateRequest().Any())
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The auction hold release request is invalid.");
        var fingerprint = HoldOperations.Fingerprint(new { request.HoldId, request.Reason });
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockAuctionKeyAsync($"key:{request.IdempotencyKey}", cancellationToken);
        var replay = await HoldOperations.Replay<ReleaseHoldResponse>(context, request.IdempotencyKey,
            "release", fingerprint, cancellationToken);
        if (replay is not null) return replay;
        var hold = await context.LockAuctionHoldAsync(request.HoldId, cancellationToken)
            ?? throw new EntityNotFoundException("HOLD_NOT_FOUND", nameof(AuctionHold), request.HoldId);
        if (hold.Status == AuctionHoldStatus.Released)
        {
            var original = await context.AuctionHoldOperations.AsNoTracking()
                .Where(item => item.HoldId == hold.Id && item.Operation == "release")
                .OrderBy(item => item.CreatedAt).FirstAsync(cancellationToken);
            if (original.RequestFingerprint != fingerprint)
                throw new BusinessRuleValidationException("IDEMPOTENCY_CONFLICT",
                    "The hold was released with a different request.", 409);
            var prior = System.Text.Json.JsonSerializer.Deserialize<ReleaseHoldResponse>(original.ResponseJson)
                ?? throw new InvalidOperationException("Stored auction hold response is invalid.");
            HoldOperations.Record(context, hold.Id, request.IdempotencyKey, "release", fingerprint, prior, 200);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return prior;
        }
        if (hold.Status != AuctionHoldStatus.Held)
            throw new BusinessRuleValidationException("HOLD_ALREADY_CAPTURED", "The hold cannot be released.", 409);
        var wallet = await context.LockWalletByIdAsync(hold.WalletId, cancellationToken)
            ?? throw new InvalidOperationException("The hold wallet was not found.");
        var before = wallet.LockedBalance;
        wallet.ReleaseLockedFunds(hold.Amount);
        hold.Status = AuctionHoldStatus.Released;
        hold.ResolvedAt = DateTimeOffset.UtcNow;
        hold.UpdatedAt = hold.ResolvedAt.Value;
        var result = new ReleaseHoldResponse(hold.Id, hold.Status.ToString(), hold.ResolvedAt.Value, wallet.AvailableBalance);
        HoldOperations.Record(context, hold.Id, request.IdempotencyKey, "release", fingerprint, result, 200);
        context.AddAuditLog(AuditAction.AuctionHoldRelease, nameof(AuctionHold), hold.Id.ToString(),
            oldValues: new { LockedBalance = before },
            newValues: new { hold.Amount, wallet.LockedBalance, request.Reason }, userId: hold.OwnerUserId.ToString());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
