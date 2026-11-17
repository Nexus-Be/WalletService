namespace Wallet.Application.Features.Holds.Commands.RefundCapturedHold;

using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class RefundCapturedHoldCommandHandler(IWalletDbContext context)
    : IRequestHandler<RefundCapturedHoldCommand, RefundCapturedHoldResponse>
{
    public async Task<RefundCapturedHoldResponse> Handle(RefundCapturedHoldCommand request, CancellationToken cancellationToken)
    {
        if (request.ValidateRequest().Any())
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The auction deposit refund request is invalid.");
        var fingerprint = HoldOperations.Fingerprint(new { request.HoldId, request.OrderId, request.Reason });
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.LockAuctionKeyAsync($"key:{request.IdempotencyKey}", cancellationToken);
        var replay = await HoldOperations.Replay<RefundCapturedHoldResponse>(context, request.IdempotencyKey, "refund", fingerprint, cancellationToken);
        if (replay is not null) return replay;
        var hold = await context.LockAuctionHoldAsync(request.HoldId, cancellationToken)
            ?? throw new EntityNotFoundException("HOLD_NOT_FOUND", nameof(AuctionHold), request.HoldId);
        var original = await context.AuctionHoldOperations.AsNoTracking()
            .Where(item => item.HoldId == hold.Id && item.Operation == "refund")
            .OrderBy(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (original is not null)
        {
            if (original.RequestFingerprint != fingerprint)
                throw new BusinessRuleValidationException("IDEMPOTENCY_CONFLICT", "The hold was refunded with a different request.", 409);
            var prior = JsonSerializer.Deserialize<RefundCapturedHoldResponse>(original.ResponseJson)
                ?? throw new InvalidOperationException("Stored auction hold response is invalid.");
            HoldOperations.Record(context, hold.Id, request.IdempotencyKey, "refund", fingerprint, prior, 200);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return prior;
        }
        var capture = await context.AuctionHoldCaptures.AsNoTracking()
            .SingleOrDefaultAsync(item => item.HoldId == hold.Id, cancellationToken);
        if (hold.Status != AuctionHoldStatus.Captured || capture?.Purpose != "ORDER_PAYMENT" || capture.OrderId != request.OrderId)
            throw new BusinessRuleValidationException("HOLD_NOT_REFUNDABLE", "The hold cannot be refunded for this order.", 409);
        var escrow = await context.AuctionDepositEscrows.SingleOrDefaultAsync(item => item.HoldId == hold.Id, cancellationToken)
            ?? throw new InvalidOperationException("The captured deposit escrow was not found.");
        if (escrow.OrderId != request.OrderId || escrow.RemainingAmount < hold.Amount)
            throw new BusinessRuleValidationException("ESCROW_INSUFFICIENT", "The deposit escrow has insufficient funds.", 409);
        var buyer = await context.LockWalletByIdAsync(hold.WalletId, cancellationToken)
            ?? throw new InvalidOperationException("The hold wallet was not found.");
        var before = buyer.Balance;
        buyer.Deposit(hold.Amount);
        escrow.RemainingAmount -= hold.Amount;
        escrow.UpdatedAt = DateTimeOffset.UtcNow;
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = Guid.NewGuid(), WalletId = buyer.Id, UserId = buyer.UserId.ToString(), Type = WalletTransactionType.AuctionDepositRefund,
            Amount = hold.Amount, Currency = buyer.Currency.ToString(), BalanceBefore = before, BalanceAfter = buyer.Balance,
            ReferenceId = hold.Id, Status = TransactionStatus.Success
        });
        hold.Status = AuctionHoldStatus.Refunded;
        hold.ResolvedAt = DateTimeOffset.UtcNow;
        hold.UpdatedAt = hold.ResolvedAt.Value;
        var result = new RefundCapturedHoldResponse(hold.Id, hold.Status.ToString(), hold.Amount, buyer.Balance);
        HoldOperations.Record(context, hold.Id, request.IdempotencyKey, "refund", fingerprint, result, 200);
        context.AddAuditLog(AuditAction.AuctionHoldRefund, nameof(AuctionHold), hold.Id.ToString(),
            oldValues: new { Balance = before }, newValues: new { hold.Amount, request.OrderId, request.Reason },
            userId: buyer.UserId.ToString());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
