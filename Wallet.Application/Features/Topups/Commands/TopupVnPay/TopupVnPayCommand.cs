namespace Wallet.Application.Features.Topups.Commands.TopupVnPay;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record TopupVnPayCommand(
    [Range(10000, 100000000, ErrorMessage = "Deposit amount must be between 10,000 and 100,000,000 VND.")]
    decimal Amount,
    string? Currency = "VND",
    string? PaymentMethod = "VNPAY",
    string? BankCode = null,
    string? ReturnUrl = null) : IRequest<TopupResponse>
{
    [JsonIgnore]
    public string UserId { get; init; } = string.Empty;

    [JsonIgnore]
    public string ClientIp { get; init; } = "127.0.0.1";
}

public sealed record TopupResponse(
    string TransactionId,
    string PaymentUrl,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);
