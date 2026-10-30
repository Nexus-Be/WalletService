namespace Wallet.Application.Services.Wallet;

using System;
using System.Collections.Generic;

public sealed record WalletResponse(
    string WalletId,
    string UserId,
    decimal Balance,
    decimal AvailableBalance,
    decimal LockedBalance,
    string Currency,
    string Status,
    DateTimeOffset UpdatedAt
);

public sealed record TopupResponse(
    string TransactionId,
    string PaymentUrl,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record WithdrawalResponse(
    string TransactionId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record TransferResponse(
    string TransactionId,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record WalletTransactionResponse(
    string Id,
    string WalletId,
    string UserId,
    string Type,
    decimal Amount,
    string Currency,
    decimal BalanceAfter,
    string Status,
    string Note,
    DateTimeOffset CreatedAt
);

public sealed record WalletTransactionPageResponse(
    IReadOnlyList<WalletTransactionResponse> Items,
    int TotalItems,
    int PageNumber,
    int PageSize,
    int TotalPages
);

public sealed record DirectPaymentResponse(
    string TransactionId,
    string OrderId,
    decimal Amount,
    string PaymentUrl,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record VnPayIpnResponse(
    string ResponseCode,
    string Message
);
