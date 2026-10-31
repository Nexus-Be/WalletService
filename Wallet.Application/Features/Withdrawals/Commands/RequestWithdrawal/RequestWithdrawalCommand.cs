namespace Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record RequestWithdrawalCommand(
    [Range(50000, 500000000, ErrorMessage = "Withdrawal amount must be between 50,000 and 500,000,000 VND.")]
    decimal Amount,
    string? Currency = "VND",
    [Required(ErrorMessage = "Bank code is required.")]
    string BankCode = "",
    [Required(ErrorMessage = "Account number is required.")]
    string AccountNumber = "") : IRequest<WithdrawalResponse>
{
    [JsonIgnore]
    public string UserId { get; init; } = string.Empty;
}

public sealed record WithdrawalResponse(
    string TransactionId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);
