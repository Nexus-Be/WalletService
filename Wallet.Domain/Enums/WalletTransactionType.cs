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
    TransferOut = 12
}