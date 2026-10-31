namespace Wallet.Application.Features.Transfers.Commands.Transfer;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record TransferCommand(
    [Required(ErrorMessage = "Recipient user ID is required.")]
    string RecipientUserId,
    [Range(1000, 100000000, ErrorMessage = "Transfer amount must be between 1,000 and 100,000,000 VND.")]
    decimal Amount,
    string? Note = null) : IRequest<TransferResponse>
{
    [JsonIgnore]
    public string SenderUserId { get; init; } = string.Empty;
}

public sealed record TransferResponse(
    string TransactionId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);
