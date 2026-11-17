namespace Wallet.Application.Features.Holds.Commands.CaptureHold;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Holds.Commands.CreateHold;

public sealed record CaptureTransfer(string ToType, Guid? ToOwnerId, decimal Amount);

public sealed record CaptureHoldCommand(string Purpose, Guid? OrderId, IReadOnlyList<CaptureTransfer> Transfers)
    : IRequest<CaptureHoldResponse>, IRequestValidation
{
    [JsonIgnore] public Guid HoldId { get; init; }
    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (HoldId == Guid.Empty || !CreateHoldCommand.ValidKey(IdempotencyKey) ||
            Transfers is null || Transfers.Count == 0 || Transfers.Count > 100)
        {
            yield return new ValidationResult("The auction hold capture request is invalid.");
            yield break;
        }
        if (Transfers.Any(t => t is null || t.Amount <= 0 || t.Amount > 9999999999999999m ||
                               t.Amount != decimal.Truncate(t.Amount) ||
                               (t.ToType == "SELLER_WALLET" ? t.ToOwnerId is null || t.ToOwnerId == Guid.Empty :
                                   t.ToType is not ("PLATFORM" or "SYSTEM_ESCROW") || t.ToOwnerId is not null)) ||
            Transfers.Select(t => (t.ToType, t.ToOwnerId)).Distinct().Count() != Transfers.Count ||
            (Purpose == "FORFEIT" ? OrderId is not null || Transfers.Any(t => t.ToType == "SYSTEM_ESCROW") :
                Purpose != "ORDER_PAYMENT" || OrderId is null || OrderId == Guid.Empty ||
                Transfers.Count != 1 || Transfers[0].ToType != "SYSTEM_ESCROW"))
            yield return new ValidationResult("The auction hold capture request is invalid.");
    }
}

public sealed record CapturedTransferResponse(string ToType, Guid? ToOwnerId, decimal Amount, Guid LedgerRef);
public sealed record CaptureHoldResponse(Guid HoldId, string Status, string Purpose, DateTimeOffset CapturedAt,
    IReadOnlyList<CapturedTransferResponse> Transfers, decimal BuyerBalanceAfter);
