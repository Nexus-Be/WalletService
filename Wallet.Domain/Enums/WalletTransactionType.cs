namespace Wallet.Domain.Enums;

public enum WalletTransactionType
{
    TopupVnpay = 0,
    PurchaseDebit = 1,
    RefundCredit = 2,
    EscrowCredit = 3,
    EscrowRelease = 4,
    WithdrawalDebit = 5,
    WithdrawalReversal = 6,
    PlatformFeeCredit = 7,
    AdjustmentCredit = 8,
    AdjustmentDebit = 9,
    DirectGatewayPayment = 10,
    TransferIn = 11,
    TransferOut = 12,
    TopupStripe = 13,
    AuctionHoldCapture = 14,
    AuctionForfeitCredit = 15,
    AuctionDepositRefund = 16,
    OrderPaymentAdjustmentDebit = 17,
    OrderPaymentAdjustmentRefund = 18,
    EscrowCancellationRefund = 19,
    PlatformSubsidyCredit = 20,
    /// <summary>An admin put money into the refund fund (a real transfer from the operating account).</summary>
    RefundFundDeposit = 21,
    /// <summary>The refund fund paid a buyer after the order had already been paid out to the seller.</summary>
    RefundFundPayout = 22,
    /// <summary>The buyer received a refund from the refund fund.</summary>
    RefundFundRefund = 23,
    /// <summary>Money taken back from the seller's available balance after a refund from the fund.</summary>
    SellerRecoveryDebit = 24,
    /// <summary>The refund fund received what was taken back from the seller.</summary>
    RefundFundRecovery = 25
}
