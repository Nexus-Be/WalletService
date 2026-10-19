namespace Wallet.Domain.Enums;

public enum WithdrawalStatus
{
    Pending = 0,
    Approved = 1,
    Processing = 2,
    Completed = 3,
    Rejected = 4,
    Failed = 5
}
