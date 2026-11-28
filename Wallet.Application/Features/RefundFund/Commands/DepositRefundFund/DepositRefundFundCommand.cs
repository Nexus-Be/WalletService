namespace Wallet.Application.Features.RefundFund.Commands.DepositRefundFund;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

/// <summary>Puts money into the refund fund. The evidence reference points to the real transfer from the operating account.</summary>
public sealed record DepositRefundFundCommand(
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")] [MoneyPrecision] decimal Amount,
    [Required(ErrorMessage = "Evidence reference is required.")] [StringLength(500, ErrorMessage = "Evidence reference must be 500 characters or fewer.")] string EvidenceRef)
    : IRequest<RefundFundDepositResult>
{
    [JsonIgnore] public Guid AdminUserId { get; init; }
}

public sealed record RefundFundDepositResult(string TransactionId, decimal Amount, decimal FundBalance);
