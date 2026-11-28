namespace Wallet.Domain.Enums;

public enum SubsidyBudgetStatus
{
    PendingApproval = 0,
    Active = 1,
    Closed = 2
}

public enum SubsidyReservationStatus
{
    Held = 0,
    Released = 1,
    Used = 2
}
