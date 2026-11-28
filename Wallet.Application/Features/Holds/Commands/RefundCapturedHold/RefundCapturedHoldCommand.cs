namespace Wallet.Application.Features.Holds.Commands.RefundCapturedHold;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Holds.Commands.CreateHold;

public sealed record RefundCapturedHoldCommand(string Reason, Guid OrderId)
    : IRequest<RefundCapturedHoldResponse>, IRequestValidation
{
    [JsonIgnore] public Guid HoldId { get; init; }
    [JsonIgnore] public string IdempotencyKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (HoldId == Guid.Empty || OrderId == Guid.Empty || !CreateHoldCommand.ValidKey(IdempotencyKey) ||
            string.IsNullOrWhiteSpace(Reason) || Reason.Length > 128)
            yield return new ValidationResult("The auction deposit refund request is invalid.");
    }
}

public sealed record RefundCapturedHoldResponse(Guid HoldId, string Status, decimal RefundedAmount,
    decimal BuyerBalanceAfter);
