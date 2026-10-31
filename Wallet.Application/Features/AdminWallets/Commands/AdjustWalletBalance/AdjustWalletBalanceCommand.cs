namespace Wallet.Application.Features.AdminWallets.Commands.AdjustWalletBalance;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record AdjustWalletBalanceCommand(
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    decimal Amount,
    [Required(ErrorMessage = "Adjustment type is required.")]
    string AdjustmentType = "Credit",
    [Required(ErrorMessage = "Reason is required.")]
    string Reason = "") : IRequest<BalanceAdjustmentResponse>
{
    [JsonIgnore]
    public Guid WalletId { get; init; }

    [JsonIgnore]
    public string AdminUserId { get; init; } = string.Empty;
}

public sealed record BalanceAdjustmentResponse(
    string AdjustmentId,
    string WalletId,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset CreatedAt);
