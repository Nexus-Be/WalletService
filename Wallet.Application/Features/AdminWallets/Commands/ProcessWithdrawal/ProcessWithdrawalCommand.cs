namespace Wallet.Application.Features.AdminWallets.Commands.ProcessWithdrawal;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record ProcessWithdrawalCommand(
    [Required(ErrorMessage = "Action is required.")]
    string Action,
    string? AdminNote = null) : IRequest<ProcessWithdrawalResponse>
{
    [JsonIgnore]
    public Guid WithdrawalId { get; init; }

    [JsonIgnore]
    public string AdminUserId { get; init; } = string.Empty;
}

public sealed record ProcessWithdrawalResponse(
    string WithdrawalId,
    string Status,
    DateTimeOffset ProcessedAt,
    string? TransactionId);
