namespace Wallet.Application.Features.Withdrawals.Commands.RequestWithdrawal;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record RequestWithdrawalCommand(
    [Range(50000, 500000000, ErrorMessage = "Withdrawal amount must be between 50,000 and 500,000,000 VND.")]
    decimal Amount,
    [Required(ErrorMessage = "Currency is required.")]
    [RegularExpression("^(?i:VND)$", ErrorMessage = "Currency must be VND.")]
    string? Currency = "VND",
    [Required(ErrorMessage = "Bank code is required.")]
    [StringLength(150, ErrorMessage = "Bank code cannot exceed 150 characters.")]
    string BankCode = "",
    [Required(ErrorMessage = "Account number is required.")]
    [StringLength(100, ErrorMessage = "Account number cannot exceed 100 characters.")]
    string AccountNumber = "",
    [Required(ErrorMessage = "Account holder name is required.")]
    [StringLength(255, ErrorMessage = "Account holder name cannot exceed 255 characters.")]
    string AccountHolderName = "") : IRequest<WithdrawalResponse>
{
    [JsonIgnore]
    public string UserId { get; init; } = string.Empty;
}

public sealed record WithdrawalResponse(
    string WithdrawalId,
    decimal Amount,
    decimal Fee,
    string Status,
    DateTimeOffset RequestedAt);