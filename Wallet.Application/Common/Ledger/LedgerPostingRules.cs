namespace Wallet.Application.Common.Ledger;

using System;
using System.Collections.Generic;
using System.Linq;
using Wallet.Domain.Entities;

/// <summary>
/// Turns a balance change into balanced journal lines. The wallet side is one control account, WALLET (the money the platform owes
/// its users); the other side depends on where the money came from or went. Each entry has one debit and one credit of the same amount,
/// so it balances by construction.
/// </summary>
public static class LedgerPostingRules
{
    public const string Wallet = "WALLET";
    public const string OpeningBalance = "OPENING_BALANCE";

    public static readonly IReadOnlyDictionary<string, string> Accounts = new Dictionary<string, string>
    {
        [Wallet] = "User wallets (money owed to users)",
        [OpeningBalance] = "Opening balances when the shadow ledger started following a wallet",
        ["EXTERNAL_GATEWAY"] = "Payment gateways (Stripe, VNPAY)",
        ["ORDER_ESCROW"] = "Order payments held until settlement",
        ["AUCTION_ESCROW"] = "Auction deposits and captured holds",
        ["AUCTION_FORFEIT"] = "Forfeited auction deposits",
        ["PLATFORM_REVENUE"] = "Platform fee revenue",
        ["PLATFORM_SUBSIDY"] = "Platform-funded voucher subsidy paid to sellers",
        ["BANK_WITHDRAWAL"] = "Withdrawals paid to bank accounts",
        ["ADJUSTMENTS"] = "Manual adjustments by administrators",
        ["TRANSFER_CLEARING"] = "Transfers between wallets",
        ["REFUND_FUND_FUNDING"] = "Money put into the refund fund from the operating account",
        ["REFUND_FUND_CLEARING"] = "Refunds paid from the refund fund and what was taken back from sellers",
        ["SUSPENSE"] = "Movements without a known counter account"
    };

    /// <summary>The counter account for a wallet transaction type name. Unknown types go to SUSPENSE so nothing is dropped.</summary>
    public static string CounterAccount(string type) => type switch
    {
        "TopupVnpay" or "TopupStripe" or "DirectGatewayPayment" => "EXTERNAL_GATEWAY",
        "PurchaseDebit" or "EscrowCredit" or "EscrowRelease" or "RefundCredit" or "EscrowCancellationRefund"
            or "OrderPaymentAdjustmentDebit" or "OrderPaymentAdjustmentRefund" => "ORDER_ESCROW",
        "AuctionHoldCapture" or "AuctionDepositRefund" => "AUCTION_ESCROW",
        "AuctionForfeitCredit" => "AUCTION_FORFEIT",
        "PlatformFeeCredit" => "PLATFORM_REVENUE",
        "PlatformSubsidyCredit" => "PLATFORM_SUBSIDY",
        "WithdrawalDebit" or "WithdrawalReversal" => "BANK_WITHDRAWAL",
        "AdjustmentCredit" or "AdjustmentDebit" => "ADJUSTMENTS",
        "TransferIn" or "TransferOut" => "TRANSFER_CLEARING",
        "RefundFundDeposit" => "REFUND_FUND_FUNDING",
        "RefundFundPayout" or "RefundFundRefund" or "SellerRecoveryDebit" or "RefundFundRecovery" => "REFUND_FUND_CLEARING",
        _ => "SUSPENSE"
    };

    /// <summary>Counter account for a pending row that may cover several transaction types ("A+B"): the first one decides.</summary>
    public static string CounterAccountForPending(string typeText) => CounterAccount(typeText.Split('+')[0]);

    public static JournalEntry? BuildEntry(LedgerPostingPending pending)
    {
        var entry = new JournalEntry
        {
            WalletId = pending.WalletId,
            PendingId = pending.Id,
            WalletTransactionId = pending.WalletTransactionId,
            Source = "Live",
            Description = pending.IsEpochOpening ? "Opening balance" : pending.Type,
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (pending.IsEpochOpening)
        {
            if (pending.OpeningBalance <= 0m) return entry; // an empty opening is a valid entry with no lines
            AddLines(entry, debit: OpeningBalance, credit: Wallet, pending.OpeningBalance, pending.WalletId);
            return entry;
        }

        var amount = Math.Abs(pending.Delta);
        if (amount == 0m) return entry;
        var counter = CounterAccountForPending(pending.Type);
        if (pending.Delta > 0m)
        {
            // The wallet gained money: the platform now owes the user more.
            AddLines(entry, debit: counter, credit: Wallet, amount, pending.WalletId);
        }
        else
        {
            AddLines(entry, debit: Wallet, credit: counter, amount, pending.WalletId);
        }

        return entry;
    }

    private static void AddLines(JournalEntry entry, string debit, string credit, decimal amount, Guid walletId)
    {
        entry.Lines.Add(new JournalLine { EntryId = entry.Id, AccountCode = debit, Debit = amount, Credit = 0m, WalletId = debit == Wallet ? walletId : null });
        entry.Lines.Add(new JournalLine { EntryId = entry.Id, AccountCode = credit, Debit = 0m, Credit = amount, WalletId = credit == Wallet ? walletId : null });
    }

    /// <summary>Net effect of an entry's lines on the wallet control account: credits increase what the platform owes the user.</summary>
    public static decimal WalletNet(IEnumerable<JournalLine> lines) =>
        lines.Where(line => line.AccountCode == Wallet).Sum(line => line.Credit - line.Debit);
}
