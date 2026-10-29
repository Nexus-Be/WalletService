namespace Wallet.Domain.Enums;

public enum AuditAction
{
    WalletCreate = 0,
    TopupRequest = 1,
    TopupSuccess = 2,
    TopupFail = 3,
    WithdrawalRequest = 4,
    WithdrawalApprove = 5,
    WithdrawalReject = 6,
    WithdrawalComplete = 7,
    BalanceAdjustment = 8
}