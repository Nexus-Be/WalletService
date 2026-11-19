namespace Wallet.Application.Features.Holds.Commands.CaptureHold;

using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

internal sealed class CaptureHoldCommandHandler(
    IWalletDbContext context,
    INotificationClient? notificationClient = null)
    : IRequestHandler<CaptureHoldCommand, CaptureHoldResponse>
{
    public async Task<CaptureHoldResponse> Handle(CaptureHoldCommand request, CancellationToken cancellationToken)
    {
        if (request.ValidateRequest().Any())
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The auction hold capture request is invalid.");
        var transfers = request.Transfers.OrderBy(t => t.ToType, StringComparer.Ordinal).ThenBy(t => t.ToOwnerId).ToArray();
        var fingerprint = HoldOperations.Fingerprint(new { request.HoldId, request.Purpose, request.OrderId, Transfers = transfers });

        CaptureHoldResponse output;
        AuctionDepositNotificationEvent? notification = null;

        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            await context.LockAuctionKeyAsync($"key:{request.IdempotencyKey}", cancellationToken);
            var replay = await HoldOperations.Replay<CaptureHoldResponse>(context, request.IdempotencyKey, "capture", fingerprint, cancellationToken);
            if (replay is not null)
            {
                var replayedHold = await context.AuctionHolds.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == replay.HoldId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                if (replayedHold is not null)
                {
                    await NotificationDispatchExtensions.TryDispatchAuctionDepositEventAsync(
                        context, notificationClient, BuildCapturedEvent(replayedHold, replay),
                        replayedHold.Id, cancellationToken);
                }

                return replay;
            }

            var hold = await context.LockAuctionHoldAsync(request.HoldId, cancellationToken)
                ?? throw new EntityNotFoundException("HOLD_NOT_FOUND", nameof(AuctionHold), request.HoldId);
            var original = await context.AuctionHoldOperations.AsNoTracking()
                .Where(item => item.HoldId == hold.Id && item.Operation == "capture")
                .OrderBy(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (original is not null)
            {
                if (original.RequestFingerprint != fingerprint)
                    throw new BusinessRuleValidationException("HOLD_ALREADY_CAPTURED_DIFFERENT", "The hold was captured with a different request.", 409);
                var prior = JsonSerializer.Deserialize<CaptureHoldResponse>(original.ResponseJson)
                    ?? throw new InvalidOperationException("Stored auction hold response is invalid.");
                HoldOperations.Record(context, hold.Id, request.IdempotencyKey, "capture", fingerprint, prior, 200);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                await NotificationDispatchExtensions.TryDispatchAuctionDepositEventAsync(
                    context, notificationClient, BuildCapturedEvent(hold, prior),
                    hold.Id, cancellationToken);

                return prior;
            }
            if (hold.Status != AuctionHoldStatus.Held)
                throw new BusinessRuleValidationException("HOLD_ALREADY_RELEASED", "The hold cannot be captured.", 409);
            if (transfers.Sum(t => t.Amount) != hold.Amount)
                throw new BusinessRuleValidationException("TRANSFERS_SUM_MISMATCH", "Transfer amounts must equal the hold amount.", 422);
            if (transfers.Any(t => t.ToOwnerId == hold.OwnerUserId))
                throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The buyer cannot receive a forfeited deposit.");

            var userIds = transfers.Where(t => t.ToOwnerId.HasValue).Select(t => t.ToOwnerId!.Value)
                .Append(hold.OwnerUserId).Distinct().OrderBy(id => id).ToArray();
            var wallets = new Dictionary<Guid, WalletAccount>();
            foreach (var userId in userIds)
                wallets.Add(userId, await context.LockWalletByUserIdAsync(userId, cancellationToken)
                    ?? throw new WalletNotFoundException(userId.ToString()));
            var buyer = wallets[hold.OwnerUserId];
            if (buyer.Id != hold.WalletId || wallets.Values.Any(w => w.Currency != CurrencyCode.Vnd))
                throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "The wallet or currency does not match the hold.");

            var before = buyer.Balance;
            buyer.CompleteLockedWithdrawal(hold.Amount);
            context.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(), WalletId = buyer.Id, UserId = buyer.UserId.ToString(),
                Type = WalletTransactionType.AuctionHoldCapture, Amount = hold.Amount, Currency = buyer.Currency.ToString(),
                BalanceBefore = before, BalanceAfter = buyer.Balance, ReferenceId = hold.Id, Status = TransactionStatus.Success
            });
            var capture = new AuctionHoldCapture
            {
                Id = Guid.NewGuid(), HoldId = hold.Id, Purpose = request.Purpose, OrderId = request.OrderId,
                RequestFingerprint = fingerprint
            };
            context.AuctionHoldCaptures.Add(capture);
            var results = new List<CapturedTransferResponse>();
            foreach (var transfer in transfers)
            {
                Guid? walletTransactionId = null;
                if (transfer.ToOwnerId is Guid recipientId)
                {
                    var recipient = wallets[recipientId];
                    var balanceBefore = recipient.Balance;
                    recipient.Deposit(transfer.Amount);
                    walletTransactionId = Guid.NewGuid();
                    context.WalletTransactions.Add(new WalletTransaction
                    {
                        Id = walletTransactionId.Value, WalletId = recipient.Id, UserId = recipient.UserId.ToString(),
                        Type = WalletTransactionType.AuctionForfeitCredit, Amount = transfer.Amount,
                        Currency = recipient.Currency.ToString(), BalanceBefore = balanceBefore, BalanceAfter = recipient.Balance,
                        ReferenceId = hold.Id, Status = TransactionStatus.Success
                    });
                }
                var entry = new AuctionCaptureTransfer
                {
                    Id = Guid.NewGuid(), CaptureId = capture.Id, RecipientType = transfer.ToType,
                    RecipientUserId = transfer.ToOwnerId, Amount = transfer.Amount, WalletTransactionId = walletTransactionId
                };
                context.AuctionCaptureTransfers.Add(entry);
                results.Add(new CapturedTransferResponse(transfer.ToType, transfer.ToOwnerId, transfer.Amount, entry.Id));
            }
            if (request.Purpose == "ORDER_PAYMENT")
                context.AuctionDepositEscrows.Add(new AuctionDepositEscrow
                {
                    Id = Guid.NewGuid(), HoldId = hold.Id, OrderId = request.OrderId!.Value,
                    OriginalAmount = hold.Amount, RemainingAmount = hold.Amount
                });
            hold.Status = AuctionHoldStatus.Captured;
            hold.ResolvedAt = DateTimeOffset.UtcNow;
            hold.UpdatedAt = hold.ResolvedAt.Value;
            output = new CaptureHoldResponse(hold.Id, hold.Status.ToString(), request.Purpose, hold.ResolvedAt.Value, results, buyer.Balance);
            HoldOperations.Record(context, hold.Id, request.IdempotencyKey, "capture", fingerprint, output, 200);
            context.AddAuditLog(AuditAction.AuctionHoldCapture, nameof(AuctionHold), hold.Id.ToString(),
                oldValues: new { Balance = before }, newValues: new { hold.Amount, request.Purpose, request.OrderId, Transfers = transfers },
                userId: buyer.UserId.ToString());
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            notification = BuildCapturedEvent(hold, output);
        }

        if (notification is not null)
        {
            await NotificationDispatchExtensions.TryDispatchAuctionDepositEventAsync(
                context, notificationClient, notification, output.HoldId, cancellationToken);
        }

        return output;
    }

    private static AuctionDepositNotificationEvent BuildCapturedEvent(AuctionHold hold, CaptureHoldResponse result)
    {
        var status = string.Equals(result.Purpose, "FORFEIT", StringComparison.Ordinal)
            ? "Forfeited"
            : "Captured";
        return new AuctionDepositNotificationEvent(
            SourceEventId: $"hold:{hold.Id:D}:{status}",
            TransactionId: hold.Id.ToString("D"),
            AuctionId: HoldOperations.ExtractAuctionId(hold.MetadataJson),
            BidderId: hold.OwnerUserId,
            Status: status,
            Amount: hold.Amount,
            Currency: "VND",
            ConfirmedAt: result.CapturedAt,
            TraceId: Activity.Current?.Id);
    }
}
