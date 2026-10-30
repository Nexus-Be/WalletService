namespace Wallet.Application.Services.Admin;

using System;

public sealed record ProcessWithdrawalResponse(
    string WithdrawalId,
    string Status,
    DateTimeOffset ProcessedAt,
    string? TransactionId
);

public sealed record BalanceAdjustmentResponse(
    string AdjustmentId,
    string WalletId,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset CreatedAt
);
