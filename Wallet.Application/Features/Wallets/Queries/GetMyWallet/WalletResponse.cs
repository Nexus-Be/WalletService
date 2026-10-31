namespace Wallet.Application.Features.Wallets.Queries.GetMyWallet;

using System;

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
