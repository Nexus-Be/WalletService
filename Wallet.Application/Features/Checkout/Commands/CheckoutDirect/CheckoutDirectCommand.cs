namespace Wallet.Application.Features.Checkout.Commands.CheckoutDirect;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record CheckoutDirectCommand(
    [Required(ErrorMessage = "Order ID is required.")]
    string OrderId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    decimal Amount,
    [Required(ErrorMessage = "Gateway is required.")]
    string Gateway,
    string? BankCode = null,
    [Required(ErrorMessage = "Return URL is required.")]
    string ReturnUrl = "") : IRequest<DirectPaymentResponse>
{
    [JsonIgnore]
    public string UserId { get; init; } = string.Empty;
}

public sealed record DirectPaymentResponse(
    string TransactionId,
    string OrderId,
    decimal Amount,
    string PaymentUrl,
    string Status,
    DateTimeOffset CreatedAt);
