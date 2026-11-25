namespace Wallet.Application.Features.RefundFund.Commands.PayRefundFundCase;

using System;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Validation;

/// <summary>
/// Refunds a buyer from the refund fund for a sub-order that was already paid out to the seller (wallet payment, whole refund only).
/// Commerce sends the decision with the ids of the admin or admins who approved it. The answer is "Refunded" (money moved, or an earlier
/// identical request is repeated) or "NeedsFinance" with a reason (nothing moved; Commerce keeps the case for a person to handle).
/// </summary>
public sealed record PayRefundFundCaseCommand(
    Guid ParentOrderId,
    [NonEmptyGuid(ErrorMessage = "Buyer ID must be a valid UUID.")] string BuyerId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")] [MoneyPrecision] decimal Amount,
    [NonEmptyGuid(ErrorMessage = "First approver must be a valid UUID.")] string FirstApproverId,
    string? SecondApproverId)
    : IRequest<RefundFundCaseResult>
{
    [System.Text.Json.Serialization.JsonIgnore] public Guid SubOrderId { get; init; }
}

public sealed record RefundFundCaseResult(
    string Outcome,
    string? Reason,
    string SubOrderId,
    decimal Amount,
    decimal SellerRecovered,
    decimal SellerShortfall,
    bool Replayed,
    string? BuyerTransactionId,
    decimal FundBalance);
