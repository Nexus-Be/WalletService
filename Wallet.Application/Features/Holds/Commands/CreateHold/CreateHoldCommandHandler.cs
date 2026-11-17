namespace Wallet.Application.Features.Holds.Commands.CreateHold;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class CreateHoldCommandHandler(IWalletDbContext context)
    : IRequestHandler<CreateHoldCommand, CreateHoldResult>
{
    public async Task<CreateHoldResult> Handle(CreateHoldCommand request, CancellationToken cancellationToken)
    {
        if (request.ValidateRequest().Any())
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The auction hold request is invalid.");
        var metadata = HoldOperations.NormalizeMetadata(request.Metadata);
        var fingerprint = HoldOperations.Fingerprint(new
        {
            request.OwnerUserId, request.ReferenceType, request.ReferenceId, request.Amount,
            Currency = "VND", Metadata = metadata
        });
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockAuctionKeyAsync($"key:{request.IdempotencyKey}", cancellationToken);
        var replay = await HoldOperations.Replay<CreateHoldResponse>(context, request.IdempotencyKey,
            "create", fingerprint, cancellationToken);
        if (replay is not null) return new CreateHoldResult(replay, false);

        var referenceLock = HoldOperations.Fingerprint(new { request.ReferenceType, request.ReferenceId });
        await context.LockAuctionKeyAsync($"reference:{referenceLock}", cancellationToken);
        var existing = await context.AuctionHolds.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.ReferenceType == request.ReferenceType &&
                item.ReferenceId == request.ReferenceId, cancellationToken);
        if (existing is not null)
        {
            using var storedMetadata = System.Text.Json.JsonDocument.Parse(existing.MetadataJson);
            if (existing.IsDeleted || existing.OwnerUserId != request.OwnerUserId ||
                existing.Amount != request.Amount ||
                HoldOperations.NormalizeMetadata(storedMetadata.RootElement) != metadata)
                throw new BusinessRuleValidationException("HOLD_REFERENCE_CONFLICT",
                    "The hold reference was already used for a different request.", 409);
            var original = await context.AuctionHoldOperations.AsNoTracking()
                .Where(item => item.HoldId == existing.Id && item.Operation == "create")
                .OrderBy(item => item.CreatedAt).FirstAsync(cancellationToken);
            var prior = System.Text.Json.JsonSerializer.Deserialize<CreateHoldResponse>(original.ResponseJson)
                ?? throw new InvalidOperationException("Stored auction hold response is invalid.");
            HoldOperations.Record(context, existing.Id, request.IdempotencyKey, "create", fingerprint, prior, 200);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CreateHoldResult(prior, false);
        }

        var wallet = await context.LockWalletByUserIdAsync(request.OwnerUserId, cancellationToken)
            ?? throw new WalletNotFoundException(request.OwnerUserId.ToString());
        if (!string.Equals(wallet.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleValidationException("WALLET_FROZEN", "The wallet is not active.", 403);
        if (wallet.Currency != CurrencyCode.Vnd)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The wallet currency must be VND.");
        var before = wallet.LockedBalance;
        wallet.LockFunds(request.Amount);
        var hold = new AuctionHold
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, OwnerUserId = wallet.UserId,
            ReferenceType = request.ReferenceType, ReferenceId = request.ReferenceId,
            Amount = request.Amount, MetadataJson = metadata
        };
        context.AuctionHolds.Add(hold);
        var result = new CreateHoldResponse(hold.Id, hold.Status.ToString(), hold.Amount,
            wallet.Balance, wallet.LockedBalance, wallet.AvailableBalance, hold.CreatedAt);
        HoldOperations.Record(context, hold.Id, request.IdempotencyKey, "create", fingerprint, result, 201);
        context.AddAuditLog(AuditAction.AuctionHoldCreate, nameof(AuctionHold), hold.Id.ToString(),
            oldValues: new { LockedBalance = before },
            newValues: new { hold.Amount, wallet.LockedBalance }, userId: wallet.UserId.ToString());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreateHoldResult(result, true);
    }
}
