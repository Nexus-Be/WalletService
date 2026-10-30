namespace Wallet.Application.Services.Wallet;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public sealed record TopupVnPayRequest(
    [property: Range(10000, 100000000, ErrorMessage = "Deposit amount must be between 10,000 and 100,000,000 VND.")]
    decimal Amount,
    string? Currency = "VND",
    string? PaymentMethod = "VNPAY",
    string? BankCode = null,
    string? ReturnUrl = null
);

public sealed record WithdrawalRequest(
    [property: Range(50000, 500000000, ErrorMessage = "Withdrawal amount must be between 50,000 and 500,000,000 VND.")]
    decimal Amount,
    string? Currency = "VND",
    [property: Required(ErrorMessage = "Bank code is required.")]
    string BankCode = "",
    [property: Required(ErrorMessage = "Account number is required.")]
    string AccountNumber = ""
);

public sealed record TransferRequest(
    [property: Required(ErrorMessage = "Recipient user ID is required.")]
    string RecipientUserId,
    [property: Range(1000, 100000000, ErrorMessage = "Transfer amount must be between 1,000 and 100,000,000 VND.")]
    decimal Amount,
    string? Note = null
);

public sealed record ListTransactionsRequest(
    [property: Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")]
    int Page = 1,
    [property: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int PageSize = 10,
    string? Type = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null
);

public sealed record DirectPaymentRequest(
    [property: Required(ErrorMessage = "Order ID is required.")]
    string OrderId,
    [property: Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    decimal Amount,
    [property: Required(ErrorMessage = "Gateway is required.")]
    string Gateway,
    string? BankCode = null,
    [property: Required(ErrorMessage = "Return URL is required.")]
    string ReturnUrl = ""
);

public sealed record ProcessVnPayIpnRequest(
    IReadOnlyDictionary<string, string> Parameters,
    string? ClientIp
);
